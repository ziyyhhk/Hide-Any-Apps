; Global in-memory state. Each entry: Map("hwnd", UInt, "exStyle", UInt, "ruleId", String, "title", String)
global HiddenEntries := []

GetHiddenJsonPath() {
    ; Path is inlined (not via GetAppDataDir()) so AHK's LocalSameAsGlobal
    ; warning doesn't fire on the cross-file function reference.
    return A_AppData . "\HideAnyWindow\hidden.json"
}

RegistryAdd(hwnd, exStyle, ruleId, title) {
    global HiddenEntries, PendingAudioForHwnd
    pid := 0
    DllCall("GetWindowThreadProcessId", "Ptr", hwnd, "UInt*", &pid)
    entry := Map("hwnd", hwnd, "exStyle", exStyle, "ruleId", ruleId, "title", title, "pid", pid, "audio", [])
    if PendingAudioForHwnd.Has(hwnd) {
        entry["audio"] := PendingAudioForHwnd[hwnd]
        PendingAudioForHwnd.Delete(hwnd)
    }
    HiddenEntries.Push(entry)
    SaveHiddenJson()
}

RegistryRemoveByHwnd(hwnd) {
    global HiddenEntries
    i := HiddenEntries.Length
    while (i >= 1) {
        if HiddenEntries[i]["hwnd"] = hwnd {
            HiddenEntries.RemoveAt(i)
        }
        i -= 1
    }
    SaveHiddenJson()
}

; Returns array of removed entries (so caller can restore them).
RegistryRemoveByRuleId(ruleId) {
    global HiddenEntries
    removed := []
    keep := []
    for entry in HiddenEntries {
        if entry["ruleId"] = ruleId
            removed.Push(entry)
        else
            keep.Push(entry)
    }
    HiddenEntries := keep
    SaveHiddenJson()
    return removed
}

; Returns array of all entries and clears the registry. Caller restores them.
RegistryDrain() {
    global HiddenEntries
    drained := HiddenEntries
    HiddenEntries := []
    SaveHiddenJson()
    return drained
}

SaveHiddenJson() {
    global HiddenEntries
    path := GetHiddenJsonPath()
    tmp := path . ".tmp"
    text := JSON.stringify(Map("entries", HiddenEntries))
    if FileExist(tmp)
        FileDelete(tmp)
    FileAppend(text, tmp, "UTF-8")
    if FileExist(path)
        FileDelete(path)
    FileMove(tmp, path)
}

; Drops entries whose window has been destroyed. Windows can close while the
; service is running; without this the in-memory list (and hidden.json) grows
; for the lifetime of the process.
PruneHiddenEntries() {
    global HiddenEntries
    if (HiddenEntries.Length = 0)
        return 0
    keep := []
    removed := 0
    for entry in HiddenEntries {
        if DllCall("IsWindow", "Ptr", entry["hwnd"], "Int")
            keep.Push(entry)
        else
            removed += 1
    }
    if (removed > 0) {
        HiddenEntries := keep
        SaveHiddenJson()
        ServiceLog("INFO", "pruned " . removed . " dead hidden-window entries")
    }
    return removed
}

; Returns array of entries from disk. Does NOT populate HiddenEntries — caller decides.
LoadHiddenJson() {
    path := GetHiddenJsonPath()
    if !FileExist(path)
        return []
    try {
        text := FileRead(path, "UTF-8")
        data := JSON.parse(text)
        return data.Has("entries") ? data["entries"] : []
    } catch as e {
        ServiceLog("WARN", "hidden.json parse failed: " . e.Message)
        return []
    }
}
