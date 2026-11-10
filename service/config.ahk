; Default config used when file is missing or unreadable.
GetDefaultConfig() {
    return Map(
        "schemaVersion", 2,
        "serviceState", "running",
        "hotkeys", GetDefaultHotkeys(),
        "rules", []
    )
}

; Global hotkeys. Values use the manager's display format ("Ctrl+Alt+H").
; Keep the defaults in sync with manager/.../Models/HotkeySettings.cs.
GetDefaultHotkeys() {
    return Map(
        "suspendAll", "Ctrl+Alt+H",
        "hideForeground", "Ctrl+Alt+Shift+H",
        "unhideLast", "Ctrl+Alt+Shift+U"
    )
}

GetConfigPath() {
    ; Path is inlined (not via GetAppDataDir()) so AHK's LocalSameAsGlobal
    ; warning doesn't fire on the cross-file function reference.
    return A_AppData . "\HideAnyWindow\config.json"
}

; LoadConfig() -> Map
; Returns parsed config or default. Logs malformed reads.
LoadConfig() {
    path := GetConfigPath()
    if !FileExist(path) {
        ServiceLog("INFO", "config.json missing — using defaults")
        return GetDefaultConfig()
    }
    try {
        text := FileRead(path, "UTF-8")
        cfg := JSON.parse(text)
        if !cfg.Has("rules") || !(cfg["rules"] is Array) {
            ServiceLog("WARN", "config.json missing 'rules' array — using defaults")
            return GetDefaultConfig()
        }
        if !cfg.Has("serviceState")
            cfg["serviceState"] := "running"
        if !cfg.Has("schemaVersion")
            cfg["schemaVersion"] := 2
        return cfg
    } catch as e {
        ServiceLog("ERROR", "config.json parse failed: " . e.Message)
        return GetDefaultConfig()
    }
}

; ==========================================================================
; Cross-process write lock
; ==========================================================================
; The service now rewrites config.json when a hotkey toggles a rule, and the
; manager writes it when the user edits the UI. Both sides serialize through
; the same named semaphore (the C# side uses "HideAnyWindow_ConfigLock" too)
; so a rename never lands in the middle of the other writer's move.
; A semaphore — not a mutex — because C# async continuations may resume on a
; different thread, and mutexes are thread-affine.

AcquireConfigLock() {
    h := DllCall("CreateSemaphoreW", "Ptr", 0, "Int", 1, "Int", 1
        , "Str", "HideAnyWindow_ConfigLock", "Ptr")
    if (h = 0)
        return 0
    ; 2 s is far longer than either writer needs; time out rather than hang.
    ; WAIT_OBJECT_0 (0) means we own the semaphore. On timeout close the handle
    ; and let the caller write unlocked — never release a lock we did not take
    ; (releasing past the max count would make ReleaseSemaphore fail).
    if (DllCall("WaitForSingleObject", "Ptr", h, "UInt", 2000, "UInt") != 0) {
        DllCall("CloseHandle", "Ptr", h)
        return 0
    }
    return h
}

ReleaseConfigLock(h) {
    if (h != 0) {
        DllCall("ReleaseSemaphore", "Ptr", h, "Int", 1, "Ptr", 0)
        DllCall("CloseHandle", "Ptr", h)
    }
}

; SaveConfig(cfg) -> bool
; Atomic write (temp + rename) under the cross-process lock, matching the
; manager's ConfigStore.SaveImmediateAsync.
SaveConfig(cfg) {
    path := GetConfigPath()
    tmp := path . ".svc.tmp"
    hLock := AcquireConfigLock()
    if (hLock = 0)
        ServiceLog("WARN", "config lock unavailable — writing unlocked")
    try {
        if FileExist(tmp)
            FileDelete(tmp)
        FileAppend(JSON.stringify(PrepareConfigForJson(cfg)), tmp, "UTF-8")
        ; Overwrite = 1 (the manager's File.Move overwrite:true equivalent).
        ; The manager reads the file too; a read that overlaps this rename can
        ; make it fail, so retry briefly instead of dropping the hotkey change.
        moved := false
        Loop 5 {
            try {
                FileMove(tmp, path, 1)
                moved := true
                break
            } catch {
                Sleep 40
            }
        }
        if (!moved) {
            ServiceLog("ERROR", "config.json rename failed after retries")
            try FileDelete(tmp)
            return false
        }
        return true
    } catch as e {
        ServiceLog("ERROR", "config.json write failed: " . e.Message)
        return false
    } finally {
        ReleaseConfigLock(hLock)
    }
}

; AutoHotkey has no native boolean type: JSON.parse turns true/false into 1/0,
; and JSON.stringify writes plain integers back out. The manager (and any other
; JSON consumer) expects a real boolean for rule.enabled, so convert to
; JSON.true / JSON.false just before serializing. Returns a shallow copy so the
; live in-memory config keeps its plain 1/0 values.
PrepareConfigForJson(cfg) {
    out := Map()
    for key, value in cfg {
        if (key = "rules" && value is Array) {
            rules := []
            for rule in value {
                if !(rule is Map) {
                    rules.Push(rule)
                    continue
                }
                copy := Map()
                for k, v in rule
                    copy[k] := v
                enabled := copy.Has("enabled") ? copy["enabled"] : true
                copy["enabled"] := IsConfigEnabled(enabled) ? JSON.true : JSON.false
                rules.Push(copy)
            }
            out[key] := rules
        } else {
            out[key] := value
        }
    }
    return out
}

; Values can be plain 1/0 from JSON.parse, strings from a hand-edited config,
; or JSON.true/JSON.false ComValue objects from an earlier normalization.
IsConfigEnabled(value, default := true) {
    if (value = "")
        return default
    if (value = 0 || value = "0" || value = "false")
        return false
    if (value = JSON.false)
        return false
    return true
}
