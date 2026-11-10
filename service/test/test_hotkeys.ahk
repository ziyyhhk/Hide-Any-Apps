#Requires AutoHotkey v2.0
; Same suppression main.ahk uses: hotkeys.ahk references symbols (CurrentConfig,
; StartupScan, ...) that live in main.ahk and are not part of this harness.
#Warn All, Off

; Headless unit tests for hotkeys.ahk. Writes a PASS/FAIL report to
; %TEMP%\haw-hotkey-tests.txt and exits 0 (pass) / 1 (fail) / 99 (error).
; Does NOT touch config.json, so it is safe to run while the service is live.
;
;   AutoHotkey64.exe service\test\test_hotkeys.ahk
;   Get-Content "$env:TEMP\haw-hotkey-tests.txt"

#Include ..\lib\JSON.ahk
#Include ..\log.ahk
#Include ..\config.ahk
#Include ..\registry.ahk
#Include ..\hider.ahk
#Include ..\hotkeys.ahk

global results := []
global reportPath := A_Temp . "\haw-hotkey-tests.txt"

Check(name, condition, detail := "") {
    global results
    if condition
        results.Push("PASS: " . name)
    else if detail = ""
        results.Push("FAIL: " . name)
    else
        results.Push("FAIL: " . name . " - " . detail)
}

WriteReport(prefix := "") {
    global results, reportPath
    text := prefix
    for r in results
        text .= r . "`n"
    if FileExist(reportPath) {
        try FileDelete(reportPath)
    }
    FileAppend(text, reportPath, "UTF-8")
}

RunTests() {
    ; --- ToAhkHotkey: display format -> AHK syntax ---
    Check("ctrl+alt single key", ToAhkHotkey("Ctrl+Alt+H") = "^!H", ToAhkHotkey("Ctrl+Alt+H"))
    Check("all modifiers + F-key", ToAhkHotkey("Ctrl+Alt+Shift+Win+F12") = "^!+#F12", ToAhkHotkey("Ctrl+Alt+Shift+Win+F12"))
    Check("digit key", ToAhkHotkey("Ctrl+Alt+1") = "^!1", ToAhkHotkey("Ctrl+Alt+1"))
    Check("lowercase modifiers", ToAhkHotkey("ctrl+alt+h") = "^!h", ToAhkHotkey("ctrl+alt+h"))
    Check("spaces tolerated", ToAhkHotkey("Ctrl + Alt + H") = "^!H", ToAhkHotkey("Ctrl + Alt + H"))
    Check("empty -> empty", ToAhkHotkey("") = "")
    Check("modifier-only -> empty", ToAhkHotkey("Ctrl+Alt") = "", ToAhkHotkey("Ctrl+Alt"))
    Check("no modifier -> empty", ToAhkHotkey("H") = "", ToAhkHotkey("H"))

    ; --- GetHotkeySettings: merge defaults into partial configs ---
    hk := GetHotkeySettings(Map("rules", []))
    Check("missing hotkeys -> defaults"
        , hk["suspendAll"] = "Ctrl+Alt+H"
        && hk["hideForeground"] = "Ctrl+Alt+Shift+H"
        && hk["unhideLast"] = "Ctrl+Alt+Shift+U")

    partial := Map("rules", [], "hotkeys", Map("suspendAll", "Ctrl+Shift+1"))
    hk := GetHotkeySettings(partial)
    Check("partial hotkeys keep overrides and fill defaults"
        , hk["suspendAll"] = "Ctrl+Shift+1"
        && hk["hideForeground"] = "Ctrl+Alt+Shift+H"
        && hk["unhideLast"] = "Ctrl+Alt+Shift+U")

    ; --- GetDefaultConfig: schema 2 includes hotkeys ---
    cfg := GetDefaultConfig()
    Check("default schema version is 2", cfg["schemaVersion"] = 2)
    Check("default config has hotkeys map"
        , Type(cfg["hotkeys"]) = "Map"
        && cfg["hotkeys"]["suspendAll"] = "Ctrl+Alt+H")
}

try {
    RunTests()
} catch as e {
    WriteReport("ERROR: " . e.Message . "`n" . e.Stack . "`n")
    ExitApp 99
}

allPass := true
for r in results {
    if InStr(r, "FAIL") || InStr(r, "ERROR")
        allPass := false
}

WriteReport()
if allPass
    ExitApp 0
else
    ExitApp 1
