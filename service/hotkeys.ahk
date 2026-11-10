; ==========================================================================
; Global hotkeys
; ==========================================================================
; The service owns the hotkeys (not the manager): it is the only process that
; is always running while hiding is active, and RegisterHotKey costs no CPU
; per keystroke (unlike a keyboard hook). Three global actions plus one
; optional toggle per rule:
;
;   suspendAll      hide/show every managed app (pause auto-hide + restore)
;   hideForeground  hide whatever window is focused right now
;   unhideLast      bring back the most recently hidden window
;   rule.hotkey     toggle that rule on/off (persisted to config.json)
;
; Hotkey values are stored in the manager's display format ("Ctrl+Alt+H") and
; translated to AHK's symbolic syntax here.

global RegisteredHotkeys := []
global HidingSuspended := false

GetHotkeySettings(cfg) {
    defaults := GetDefaultHotkeys()
    out := Map()
    if (cfg != "" && cfg.Has("hotkeys") && Type(cfg["hotkeys"]) = "Map") {
        for name, def in defaults
            out[name] := cfg["hotkeys"].Has(name) ? cfg["hotkeys"][name] : def
    } else {
        for name, def in defaults
            out[name] := def
    }
    return out
}

; "Ctrl+Alt+Shift+H" -> "^!+H". Returns "" for empty/invalid input.
ToAhkHotkey(display) {
    if (display = "" || Type(display) != "String")
        return ""
    hk := Trim(display)
    hk := StrReplace(hk, " ")
    ; Must be Modifier(+Modifier)*+Key — catches "Ctrl+Alt" (no key) and
    ; bare-key strings before they reach RegisterHotKey.
    if !RegExMatch(hk, "i)^(?:(?:Ctrl|Control|Alt|Shift|Win|Windows)\+)+[A-Za-z0-9]+$")
        return ""
    hk := StrReplace(hk, "Ctrl+", "^")
    hk := StrReplace(hk, "Control+", "^")
    hk := StrReplace(hk, "Alt+", "!")
    hk := StrReplace(hk, "Shift+", "+")
    hk := StrReplace(hk, "Windows+", "#")
    hk := StrReplace(hk, "Win+", "#")
    ; No modifier word may survive (e.g. a malformed "Ctrl+Alt").
    if RegExMatch(hk, "i)(Ctrl|Control|Alt|Shift|Win)")
        return ""
    ; Our format always requires at least one modifier; a bare key would
    ; swallow normal typing, so refuse it.
    if !RegExMatch(hk, "^[\^!+#].+")
        return ""
    return hk
}

UnregisterHotkeys() {
    global RegisteredHotkeys
    for hk in RegisteredHotkeys {
        try Hotkey(hk, "Off")
    }
    RegisteredHotkeys := []
}

RegisterOneHotkey(display, callback, label) {
    global RegisteredHotkeys
    ahkKey := ToAhkHotkey(display)
    if (ahkKey = "")
        return
    try {
        Hotkey(ahkKey, callback, "On")
        RegisteredHotkeys.Push(ahkKey)
        ServiceLog("INFO", "hotkey registered: " . display . " (" . label . ")")
    } catch as e {
        ; Invalid syntax or another app already owns the combo. Never fatal:
        ; the rest of the service keeps working without this binding.
        ServiceLog("WARN", "hotkey registration failed: " . display
            . " (" . label . "): " . e.Message)
    }
}

RegisterHotkeys(cfg) {
    UnregisterHotkeys()
    hk := GetHotkeySettings(cfg)
    RegisterOneHotkey(hk["suspendAll"], OnSuspendHotkey, "suspendAll")
    RegisterOneHotkey(hk["hideForeground"], OnHideForegroundHotkey, "hideForeground")
    RegisterOneHotkey(hk["unhideLast"], OnUnhideLastHotkey, "unhideLast")

    if (cfg != "" && cfg.Has("rules")) {
        for rule in cfg["rules"] {
            if rule.Has("hotkey") && rule["hotkey"] != ""
                RegisterOneHotkey(rule["hotkey"], OnRuleHotkey.Bind(rule["id"])
                    , "rule:" . rule["id"])
        }
    }
}

; Persist an in-memory config change (hotkey toggle) and mark it as seen so
; the 1 Hz watcher doesn't reload our own write.
PersistConfig() {
    global CurrentConfig, LastConfigMTime
    SaveConfig(CurrentConfig)
    path := GetConfigPath()
    LastConfigMTime := FileExist(path) ? FileGetTime(path, "M") : ""
}

; --------------------------------------------------------------------------
; Hotkey handlers
; --------------------------------------------------------------------------

OnSuspendHotkey(*) {
    global HidingSuspended, CurrentConfig
    HidingSuspended := !HidingSuspended
    if (HidingSuspended) {
        drained := RegistryDrain()
        for entry in drained
            RestoreWindowFromEntry(entry)
        ServiceLog("INFO", "hotkey: hiding suspended — " . drained.Length
            . " window(s) restored")
    } else {
        ServiceLog("INFO", "hotkey: hiding resumed")
        if (CurrentConfig != "")
            StartupScan(CurrentConfig)
    }
}

OnHideForegroundHotkey(*) {
    hwnd := DllCall("GetForegroundWindow", "Ptr")
    if (hwnd = 0)
        return
    if IsAlreadyHidden(hwnd) {
        ServiceLog("INFO", "hotkey: foreground window is already hidden")
        return
    }

    exe := GetExeBasenameForHwnd(hwnd)
    if (exe = "") {
        ServiceLog("WARN", "hotkey: could not resolve foreground process")
        return
    }
    cls := WinGetClass("ahk_id " . hwnd)
    if IsProtectedWindow(hwnd, exe, cls) {
        ServiceLog("WARN", "hotkey: refusing to hide protected window exe=" . exe
            . " class=" . cls)
        return
    }

    title := WinGetTitle("ahk_id " . hwnd)
    result := TryHideWindow(hwnd)
    if (result["ok"]) {
        RegistryAdd(hwnd, result["exStyle"], "__manual__", title)
        ServiceLog("INFO", "hotkey: hid foreground hwnd=" . hwnd . " exe=" . exe
            . " title=" . title)
    } else {
        ServiceLog("WARN", "hotkey: foreground hide failed hwnd=" . hwnd
            . " exe=" . exe . " reason=" . result["reason"])
    }
}

OnUnhideLastHotkey(*) {
    global HiddenEntries, CurrentConfig
    if (HiddenEntries.Length = 0) {
        ServiceLog("INFO", "hotkey: unhide-last ignored — nothing is hidden")
        return
    }

    entry := HiddenEntries.Pop()
    SaveHiddenJson()
    RestoreWindowFromEntry(entry)
    ServiceLog("INFO", "hotkey: restored last hidden hwnd=" . entry["hwnd"]
        . " ruleId=" . entry["ruleId"])

    ; If a rule hid it, turn that rule off too. Otherwise the app would be
    ; hidden again the next time it shows a window, which is surprising after
    ; an explicit "bring it back" keypress.
    ruleId := entry["ruleId"]
    if (ruleId != "" && ruleId != "__manual__" && CurrentConfig != "") {
        for rule in CurrentConfig["rules"] {
            if rule.Has("id") && rule["id"] = ruleId
                && rule.Has("enabled") && rule["enabled"] {
                rule["enabled"] := false
                RebuildEnabledRuleIndex(CurrentConfig)
                ServiceLog("INFO", "hotkey: disabled ruleId=" . ruleId
                    . " as part of unhide-last")
                PersistConfig()
                break
            }
        }
    }
}

OnRuleHotkey(ruleId, *) {
    ToggleRuleById(ruleId)
}

ToggleRuleById(ruleId) {
    global CurrentConfig, HidingSuspended
    if (CurrentConfig = "" || ruleId = "")
        return

    rule := ""
    for r in CurrentConfig["rules"] {
        if r.Has("id") && r["id"] = ruleId {
            rule := r
            break
        }
    }
    if (rule = "") {
        ServiceLog("WARN", "hotkey: ruleId=" . ruleId . " not found in config")
        return
    }

    enabled := rule.Has("enabled") && rule["enabled"]
    rule["enabled"] := !enabled
    RebuildEnabledRuleIndex(CurrentConfig)

    if (rule["enabled"]) {
        ServiceLog("INFO", "hotkey: ruleId=" . ruleId . " enabled")
        if !HidingSuspended
            HideWindowsForRule(rule)
    } else {
        ServiceLog("INFO", "hotkey: ruleId=" . ruleId . " disabled")
        removed := RegistryRemoveByRuleId(ruleId)
        for entry in removed
            RestoreWindowFromEntry(entry)
    }
    PersistConfig()
}

; Target scan for a single rule. Cheaper than StartupScan when a rule-hotkey
; turns one app back on with many windows open.
HideWindowsForRule(rule) {
    if !rule.Has("exe") || rule["exe"] = ""
        return
    exe := StrLower(rule["exe"])
    if (exe = "hideanywindowmanager.exe")
        return
    ; Hidden windows are helper/tray windows the app deliberately keeps
    ; invisible; hiding and later "restoring" them would make them appear.
    DetectHiddenWindows false
    ids := WinGetList("ahk_exe " . exe)
    DetectHiddenWindows true
    for hwnd in ids {
        if IsAlreadyHidden(hwnd)
            continue
        cls := WinGetClass("ahk_id " . hwnd)
        if IsProtectedWindow(hwnd, exe, cls)
            continue
        title := WinGetTitle("ahk_id " . hwnd)
        result := TryHideWindow(hwnd)
        if (result["ok"]) {
            RegistryAdd(hwnd, result["exStyle"], rule["id"], title)
            ServiceLog("INFO", "hotkey-hide hwnd=" . hwnd . " class=" . cls
                . " ruleId=" . rule["id"] . " title=" . title)
        } else {
            ServiceLog("WARN", "hotkey-hide failed hwnd=" . hwnd . " class=" . cls
                . " ruleId=" . rule["id"] . " reason=" . result["reason"])
        }
    }
}

; Windows the user could not get back if they vanished: the manager, the
; shell/desktop, and the taskbar. Keep this list intentionally small.
IsProtectedWindow(hwnd, exe, cls) {
    if (exe = "hideanywindowmanager.exe")
        return true
    if (cls = "Shell_TrayWnd" || cls = "Shell_SecondaryTrayWnd"
        || cls = "Progman" || cls = "WorkerW")
        return true
    return false
}
