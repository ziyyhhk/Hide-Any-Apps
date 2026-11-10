; ===========================================================================
; Per-application audio muting via WASAPI (Windows Core Audio)
; ===========================================================================
; WinHide removes the window from screen/taskbar/Alt-Tab, but the app keeps
; running — and keeps playing sounds (Geometry Dash stayed audible while
; hidden). This module mutes the hidden process's WASAPI audio sessions (the
; same per-app sessions the Windows volume mixer shows) and restores the
; exact previous mute states on unhide or service shutdown.
;
; Sessions are tracked by their instance identifier so a session that already
; existed before we hid is restored to its own prior mute state.

global CLSID_MMDeviceEnumerator  := "{BCDE0395-E52F-467C-8E3D-C4579291692E}"
global IID_IMMDeviceEnumerator   := "{A95664D2-9614-4F35-A746-DE8DB63617E6}"
global IID_IMMDevice             := "{D666063F-1587-4E43-81F1-B948E807363F}"
global IID_IAudioSessionManager2 := "{77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F}"
global IID_IAudioSessionEnumerator := "{E2F5BB11-0570-40CA-ACDD-3AA01277DEE8}"
global IID_IAudioSessionControl2 := "{BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D}"
global IID_ISimpleAudioVolume    := "{87CE5498-68D6-44E5-9215-6DA47EF883D8}"

; sessionInstanceId -> Map("pid", UInt, "mute", Int)
global MutedAudioSessions := Map()

; hwnd -> Array of Map("id", String, "mute", Int). Filled by AudioMuteForHwnd,
; consumed by RegistryAdd to persist restore info in hidden.json so a crashed
; service can still unmute correctly on next start.
global PendingAudioForHwnd := Map()

AudioGuid(s) {
    buf := Buffer(16, 0)
    if (DllCall("ole32\CLSIDFromString", "WStr", s, "Ptr", buf, "Int") != 0) {
        ServiceLog("WARN", "audio: CLSIDFromString failed for " s)
        return 0
    }
    return buf
}

AudioQI(ptr, iid) {
    buf := AudioGuid(iid)
    out := 0
    if (ComCall(0, ptr, "Ptr", buf, "Ptr*", &out, "Int") != 0 || !out)
        return 0
    return out
}

AudioRelease(ptr) {
    if (ptr)
        ComCall(2, ptr)
}

; Apartment-threaded COM (the same mode used for the shell COM objects).
AudioCoInitialize() {
    static initialized := false
    if (initialized)
        return
    DllCall("ole32\CoInitializeEx", "Ptr", 0, "UInt", 0x2)  ; COINIT_APARTMENTTHREADED
    initialized := true
}

; Collect audio sessions. Pass pid = "" to collect from every session.
; Returns Array of Map("id", String|"", "pid", UInt, "vol", Ptr, "mute", Int).
; Caller must AudioRelease each "vol". Sessions with no readable pid are
; returned as pid=0.
CollectAudioSessions(pid) {
    global CLSID_MMDeviceEnumerator, IID_IMMDeviceEnumerator, IID_IMMDevice
    global IID_IAudioSessionManager2, IID_IAudioSessionEnumerator
    global IID_IAudioSessionControl2, IID_ISimpleAudioVolume
    result := []
    AudioCoInitialize()

    enum := 0
    hr := DllCall("ole32\CoCreateInstance"
        , "Ptr", AudioGuid(CLSID_MMDeviceEnumerator), "Ptr", 0, "UInt", 23
        , "Ptr", AudioGuid(IID_IMMDeviceEnumerator), "Ptr*", &enum, "Int")
    if (hr != 0 || !enum) {
        ServiceLog("WARN", "audio: CoCreateInstance failed hr=" Format("0x{:X}", hr))
        return result
    }

    device := 0
    ; eRender (0) + eConsole (0): the default playback device where game audio lives.
    hr := ComCall(4, enum, "Int", 0, "Int", 0, "Ptr*", &device, "Int")
    AudioRelease(enum)
    if (hr != 0 || !device) {
        ServiceLog("WARN", "audio: GetDefaultAudioEndpoint failed hr=" Format("0x{:X}", hr))
        return result
    }

    sessionMgr := 0
    hr := ComCall(3, device, "Ptr", AudioGuid(IID_IAudioSessionManager2), "UInt", 23
        , "Ptr", 0, "Ptr*", &sessionMgr, "Int")  ; IMMDevice::Activate
    AudioRelease(device)
    if (hr != 0 || !sessionMgr) {
        ServiceLog("WARN", "audio: Activate(IAudioSessionManager2) failed hr=" Format("0x{:X}", hr))
        return result
    }

    sessEnum := 0
    hr := ComCall(5, sessionMgr, "Ptr*", &sessEnum, "Int")
    if (hr != 0 || !sessEnum) {
        ServiceLog("WARN", "audio: GetSessionEnumerator failed hr=" Format("0x{:X}", hr))
        AudioRelease(sessionMgr)
        return result
    }

    count := 0
    ComCall(3, sessEnum, "Int*", &count, "Int")
    Loop count {
        ctrl := 0
        hr := ComCall(4, sessEnum, "Int", A_Index - 1, "Ptr*", &ctrl, "Int")
        if (hr != 0 || !ctrl)
            continue

        ctl2 := AudioQI(ctrl, IID_IAudioSessionControl2)
        sessPid := 0
        instanceId := ""
        if (ctl2) {
            ComCall(14, ctl2, "UInt*", &sessPid, "Int")  ; GetProcessId
            pstr := 0
            ComCall(13, ctl2, "Ptr*", &pstr, "Int")  ; GetSessionInstanceIdentifier
            if (pstr) {
                instanceId := StrGet(pstr, "UTF-16")
                DllCall("ole32\CoTaskMemFree", "Ptr", pstr)
            }
        }

        if (pid != "" && sessPid != pid) {
            AudioRelease(ctl2)
            AudioRelease(ctrl)
            continue
        }

        vol := AudioQI(ctrl, IID_ISimpleAudioVolume)
        mute := -1
        if (vol)
            ComCall(6, vol, "Int*", &mute, "Int")  ; GetMute

        result.Push(Map("id", instanceId, "pid", sessPid, "vol", vol, "mute", mute))
        AudioRelease(ctl2)
        AudioRelease(ctrl)
    }

    AudioRelease(sessEnum)
    AudioRelease(sessionMgr)
    return result
}

AudioSetMute(vol, mute) {
    ; ISimpleAudioVolume::SetMute(BOOL, NULL context)
    return ComCall(5, vol, "Int", mute, "Ptr", 0, "Int")
}

; Mute audio sessions for the window's process. Records restore info in
; PendingAudioForHwnd (consumed by RegistryAdd). Returns count of sessions.
AudioMuteForHwnd(hwnd) {
    global PendingAudioForHwnd, MutedAudioSessions
    pid := 0
    DllCall("GetWindowThreadProcessId", "Ptr", hwnd, "UInt*", &pid)
    if (pid = 0)
        return 0
    records := []
    count := 0
    sessions := CollectAudioSessions(pid)
    for s in sessions {
        id := s["id"]
        if (id = "" || !s["vol"]) {
            AudioRelease(s["vol"])
            continue
        }
        if !MutedAudioSessions.Has(id)
            MutedAudioSessions[id] := Map("pid", pid, "mute", s["mute"])
        AudioSetMute(s["vol"], 1)
        count++
        AudioRelease(s["vol"])
        records.Push(Map("id", id, "mute", s["mute"]))
    }
    PendingAudioForHwnd[hwnd] := records
    if (count > 0)
        ServiceLog("INFO", "audio: muted " count " session(s) for pid=" pid)
    return count
}

; Restore audio sessions referenced by an array of Map("id", ...) records.
; Used by restore paths (whole-window unhide, service shutdown, crash recovery).
AudioRestoreRecords(records) {
    global MutedAudioSessions
    count := 0
    if (records.Length = 0)
        return 0
    wanted := Map()
    for r in records {
        if (r.Has("id") && r["id"] != "")
            wanted[r["id"]] := r.Has("mute") ? r["mute"] : 0
    }
    if (wanted.Count = 0)
        return 0
    sessions := CollectAudioSessions("")
    for s in sessions {
        id := s["id"]
        if (wanted.Has(id) && s["vol"]) {
            AudioSetMute(s["vol"], wanted[id])
            MutedAudioSessions.Delete(id)
            count++
        }
        AudioRelease(s["vol"])
    }
    if (count > 0)
        ServiceLog("INFO", "audio: restored " count " session(s)")
    return count
}

; Restore everything the service has muted (service shutdown/crash path).
AudioRestoreAll() {
    global MutedAudioSessions
    count := 0
    records := []
    for id, rec in MutedAudioSessions
        records.Push(Map("id", id, "mute", rec["mute"]))
    return AudioRestoreRecords(records)
}
