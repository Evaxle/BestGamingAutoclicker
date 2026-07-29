#NoEnv
#MaxThreadsPerHotkey 2
#UseHook
SendMode Input

SetWorkingDir %A_ScriptDir%

dataDir := A_ScriptDir . "\data"
runtimeStateFile := dataDir . "\runtime_state.ini"
activeProfileFile := dataDir . "\active_profile.txt"

if !FileExist(dataDir)
    FileCreateDir, %dataDir%

currentProfile := "default"
mode := "spam"
triggerCPS := 5
turboCPS := 70
stopDelay := 1000

holdDelay := 200
dblInterval := 300
holdActivation := "normal"

waitButton := ""
waitEnabled := false
universalEnabled := false

clickTimes := []
turboMode := false
stopTimer := 0
holdStart := 0
lastHoldClick := 0

ValidateSettings() {
    global mode, triggerCPS, turboCPS, stopDelay, holdDelay, dblInterval, holdActivation
    if (mode != "spam" && mode != "hold")
        return false
    if (triggerCPS < 1 || triggerCPS > 200)
        return false
    if (turboCPS < 1 || turboCPS > 500)
        return false
    if (stopDelay < 0 || stopDelay > 10000)
        return false
    if (holdDelay < 0 || holdDelay > 10000)
        return false
    if (dblInterval < 0 || dblInterval > 5000)
        return false
    if (holdActivation != "normal" && holdActivation != "double-click")
        return false
    return true
}

ApplyDefaults() {
    global mode, triggerCPS, turboCPS, stopDelay, holdDelay, dblInterval, holdActivation
    global waitButton, waitEnabled, universalEnabled
    mode := "spam"
    triggerCPS := 5
    turboCPS := 70
    stopDelay := 1000
    holdDelay := 200
    dblInterval := 300
    holdActivation := "normal"
    waitButton := ""
    waitEnabled := false
    universalEnabled := false
}

LoadProfile(profileName) {
    global dataDir, mode, triggerCPS, turboCPS, stopDelay
    global holdDelay, dblInterval, holdActivation
    global waitButton, waitEnabled, universalEnabled
    global currentProfile

    currentProfile := profileName
    filePath := dataDir . "\" . profileName . ".ini"
    if !FileExist(filePath)
        return false

    IniRead, mode, %filePath%, Settings, mode, %mode%
    IniRead, triggerCPS, %filePath%, Settings, triggerCPS, %triggerCPS%
    IniRead, turboCPS, %filePath%, Settings, turboCPS, %turboCPS%
    IniRead, stopDelay, %filePath%, Settings, stopDelay, %stopDelay%
    IniRead, holdDelay, %filePath%, Settings, holdDelay, %holdDelay%
    IniRead, dblInterval, %filePath%, Settings, dblInterval, %dblInterval%
    IniRead, holdActivation, %filePath%, Settings, holdActivation, %holdActivation%
    IniRead, waitButton, %filePath%, Settings, waitButton, %waitButton%
    IniRead, waitEnabled, %filePath%, Settings, waitEnabled, %waitEnabled%
    IniRead, universalEnabled, %filePath%, Settings, universalEnabled, %universalEnabled%

    if !ValidateSettings()
        return false

    return true
}

ReadActiveProfile() {
    global currentProfile, activeProfileFile
    if FileExist(activeProfileFile) {
        FileRead, profileName, %activeProfileFile%
        profileName := Trim(profileName)
        if (profileName != "")
            currentProfile := profileName
    }
    return currentProfile
}

ReadRuntimeState() {
    global currentProfile, runtimeStateFile
    if FileExist(runtimeStateFile) {
        IniRead, enabled, %runtimeStateFile%, Runtime, enabled, 0
        IniRead, profileName, %runtimeStateFile%, Runtime, profileName, %currentProfile%
        if (profileName != "")
            currentProfile := profileName
        return enabled
    }
    return 0
}

RefreshRuntimeState() {
    global universalEnabled, turboMode, currentProfile
    enabled := ReadRuntimeState()
    if (enabled) {
        if (ReadActiveProfile() != currentProfile || !universalEnabled) {
            if (!LoadProfile(currentProfile)) {
                ApplyDefaults()
                universalEnabled := false
                return
            }
        }
        universalEnabled := true
    } else {
        universalEnabled := false
        turboMode := false
        SetTimer, TurboClick, Off
    }
}

ReadActiveProfile()
RefreshRuntimeState()
SetTimer, RefreshRuntimeState, 250

~*LButton::
    if (!universalEnabled)
        return

    if (mode = "hold") {
        if (holdActivation = "double-click") {
            t := A_TickCount
            if (t - lastHoldClick <= dblInterval && !turboMode) {
                turboMode := true
                SetTimer, TurboClick, % (1000 / turboCPS)
            }
            lastHoldClick := t
            return
        }

        if (waitEnabled && waitButton != "") {
            if !GetKeyState(waitButton, "P")
                return
        }

        if (!turboMode) {
            holdStart := A_TickCount
            SetTimer, HoldCheck, 10
        }
        return
    }

    if (waitEnabled && waitButton != "") {
        if !GetKeyState(waitButton, "P")
            return
    }

    t := A_TickCount
    clickTimes.Push(t)
    Loop {
        if (clickTimes[1] < t - 1000)
            clickTimes.RemoveAt(1)
        else
            break
    }
    cps := clickTimes.Length()
    if (cps >= triggerCPS && !turboMode) {
        turboMode := true
        SetTimer, TurboClick, % (1000 / turboCPS)
    }
return

HoldCheck:
    if (!universalEnabled)
        return

    if !GetKeyState("LButton", "P") {
        SetTimer, HoldCheck, Off
        return
    }

    if (waitEnabled && waitButton != "") {
        if !GetKeyState(waitButton, "P")
            return
    }

    if (A_TickCount - holdStart >= holdDelay && !turboMode) {
        turboMode := true
        SetTimer, TurboClick, % (1000 / turboCPS)
        SetTimer, HoldCheck, Off
    }
return

~*LButton Up::
    if (!universalEnabled)
        return

    if (mode = "hold") {
        turboMode := false
        SetTimer, TurboClick, Off
        return
    }

    stopTimer := A_TickCount
    SetTimer, StopTurbo, 20
return

StopTurbo:
    if (!universalEnabled)
        return

    if (A_TickCount - stopTimer >= stopDelay) {
        turboMode := false
        SetTimer, TurboClick, Off
        SetTimer, StopTurbo, Off
    }
return

TurboClick:
    if (!universalEnabled)
        return

    if (turboMode && GetKeyState("LButton", "P"))
        Click
return
