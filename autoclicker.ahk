#NoEnv
#MaxThreadsPerHotkey 2
#UseHook
SendMode Input

dataDir := A_ScriptDir . "\data"
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

Gui, Color, 0xC0C0C0
Gui, Font, s9, MS Sans Serif
Gui, Margin, 8, 8

Gui, Add, GroupBox, w360 h70, Profiles
Gui, Add, Text, x20 y35, Profile:
Gui, Add, DropDownList, vProfileChoice x80 y30 w150 gProfileSwap
GuiControl,, ProfileChoice, default
Gui, Add, Button, gCreateProfile x240 y28 w50 h22, New
Gui, Add, Button, gDeleteProfile x295 y28 w50 h22, Delete

Gui, Add, GroupBox, x8 y80 w360 h170, Mode & Settings
Gui, Add, Text, x20 y105, Mode:
Gui, Add, DropDownList, vModeChoice x80 y100 w100 gModeSwap, spam|hold

Gui, Add, Text, x20 y130 vTrigLabel, Trigger CPS:
Gui, Add, Edit, vTriggerCPS x110 y126 w60, %triggerCPS%

Gui, Add, Text, x180 y130 vTurboLabel, Turbo CPS:
Gui, Add, Edit, vTurboCPS x250 y126 w60, %turboCPS%

Gui, Add, Text, x20 y155 vDelayLabel, Stop Delay:
Gui, Add, Edit, vStopDelay x110 y151 w60, %stopDelay%

Gui, Add, Text, x20 y180 vHoldDelayLabel, Hold Delay:
Gui, Add, Edit, vHoldDelay x110 y176 w60, %holdDelay%

Gui, Add, Text, x180 y180 vDblLabel, Double Interval:
Gui, Add, Edit, vDblInterval x250 y176 w60, %dblInterval%

Gui, Add, Text, x20 y205 vHoldActLabel, Hold Mode:
Gui, Add, DropDownList, vHoldActivation x110 y200 w120 gHoldActSwap, normal|double-click

Gui, Add, GroupBox, x8 y255 w360 h80 vWaitGroup, Wait For Button
Gui, Add, Text, x20 y280, Button:
Gui, Add, Edit, vWaitDisplay x80 y276 w80 ReadOnly, None
Gui, Add, Button, gSelectButton x170 y274 w60 h22, Select
Gui, Add, CheckBox, vWaitEnabled x240 y278, Enable

Gui, Add, CheckBox, vUniversalEnabled x8 y340, Enable Autoclicker

Gui, Add, GroupBox, x8 y365 w360 h70, Status
Gui, Add, Text, x20 y390, Status:
Gui, Add, Text, vStatusText x80 y390 w260, Idle

Gui, Add, Button, gSaveSettings x110 y435 w140 h24, Save Settings
Gui, Show,, Turbo Autoclicker

RefreshProfiles() {
    global dataDir
    profiles := "default"
    Loop, Files, %dataDir%\*.ini
    {
        name := RegExReplace(A_LoopFileName, "\.ini$")
        if (name != "default")
            profiles .= "|" name
    }
    GuiControl,, ProfileChoice, %profiles%
}

ProfileSwap:
    Gui, Submit, NoHide
    currentProfile := ProfileChoice
    LoadProfile(currentProfile)
return

CreateProfile:
    InputBox, newName, New Profile, Enter new profile name:, , 240, 140
    if (ErrorLevel = "Cancel" || newName = "")
        return
    filePath := dataDir . "\" . newName . ".ini"
    if FileExist(filePath)
        return
    FileAppend,, %filePath%
    RefreshProfiles()
    GuiControl,, ProfileChoice, %newName%
    currentProfile := newName
    SaveProfile(currentProfile)
return

DeleteProfile:
    Gui, Submit, NoHide
    if (ProfileChoice = "default")
        return
    filePath := dataDir . "\" . ProfileChoice . ".ini"
    if FileExist(filePath)
        FileDelete, %filePath%
    RefreshProfiles()
    GuiControl,, ProfileChoice, default
    currentProfile := "default"
    LoadProfile("default")
return

LoadProfile(profileName) {
    global dataDir, mode, triggerCPS, turboCPS, stopDelay
    global holdDelay, dblInterval, holdActivation
    global waitButton, waitEnabled, universalEnabled

    filePath := dataDir . "\" . profileName . ".ini"
    if !FileExist(filePath)
        return

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

    GuiControl,, ModeChoice, %mode%
    GuiControl,, TriggerCPS, %triggerCPS%
    GuiControl,, TurboCPS, %turboCPS%
    GuiControl,, StopDelay, %stopDelay%
    GuiControl,, HoldDelay, %holdDelay%
    GuiControl,, DblInterval, %dblInterval%
    GuiControl,, HoldActivation, %holdActivation%
    GuiControl,, WaitDisplay, % (waitButton = "" ? "None" : waitButton)
    GuiControl,, WaitEnabled, % (waitEnabled ? 1 : 0)
    GuiControl,, UniversalEnabled, % (universalEnabled ? 1 : 0)

    Gosub, ModeSwap
    Gosub, HoldActSwap
}

SaveProfile(profileName) {
    global dataDir, mode, triggerCPS, turboCPS, stopDelay
    global holdDelay, dblInterval, holdActivation
    global waitButton, waitEnabled, universalEnabled

    filePath := dataDir . "\" . profileName . ".ini"

    IniWrite, %mode%, %filePath%, Settings, mode
    IniWrite, %triggerCPS%, %filePath%, Settings, triggerCPS
    IniWrite, %turboCPS%, %filePath%, Settings, turboCPS
    IniWrite, %stopDelay%, %filePath%, Settings, stopDelay
    IniWrite, %holdDelay%, %filePath%, Settings, holdDelay
    IniWrite, %dblInterval%, %filePath%, Settings, dblInterval
    IniWrite, %holdActivation%, %filePath%, Settings, holdActivation
    IniWrite, %waitButton%, %filePath%, Settings, waitButton
    IniWrite, %waitEnabled%, %filePath%, Settings, waitEnabled
    IniWrite, %universalEnabled%, %filePath%, Settings, universalEnabled
}

ModeSwap:
    Gui, Submit, NoHide
    mode := ModeChoice

    if (mode = "hold") {
        GuiControl, Hide, TrigLabel
        GuiControl, Hide, TriggerCPS
        GuiControl, Hide, DelayLabel
        GuiControl, Hide, StopDelay

        GuiControl, Show, TurboLabel
        GuiControl, Show, TurboCPS
        GuiControl, Show, HoldDelayLabel
        GuiControl, Show, HoldDelay
        GuiControl, Show, DblLabel
        GuiControl, Show, DblInterval
        GuiControl, Show, HoldActLabel
        GuiControl, Show, HoldActivation

        if (holdActivation = "normal")
            GuiControl, Show, WaitGroup
        else
            GuiControl, Hide, WaitGroup
    } else {
        GuiControl, Show, TrigLabel
        GuiControl, Show, TriggerCPS
        GuiControl, Show, TurboLabel
        GuiControl, Show, TurboCPS
        GuiControl, Show, DelayLabel
        GuiControl, Show, StopDelay

        GuiControl, Hide, HoldDelayLabel
        GuiControl, Hide, HoldDelay
        GuiControl, Hide, DblLabel
        GuiControl, Hide, DblInterval
        GuiControl, Hide, HoldActLabel
        GuiControl, Hide, HoldActivation
        GuiControl, Hide, WaitGroup
    }
return

HoldActSwap:
    Gui, Submit, NoHide
    if (HoldActivation = "double-click") {
        GuiControl, Hide, WaitGroup
    } else {
        GuiControl, Show, WaitGroup
        GuiControl, Enable, WaitDisplay
        GuiControl, Enable, WaitEnabled
        GuiControl, Enable, SelectButton
    }
return

SelectButton:
    ih := InputHook("L1")
    ih.Start()
    ih.Wait()
    key := ih.Input
    if (key = "") {
        waitButton := ""
        GuiControl,, WaitDisplay, None
        return
    }
    waitButton := key
    GuiControl,, WaitDisplay, %waitButton%
    AutoSave()
return

~*LButton::
    if (!universalEnabled)
        return

    if (mode = "hold") {
        if (holdActivation = "double-click") {
            t := A_TickCount
            if (t - lastHoldClick <= dblInterval && !turboMode) {
                turboMode := true
                GuiControl,, StatusText, Turbo Mode ON
                SetTimer, TurboClick, % (1000 / turboCPS)
            }
            lastHoldClick := t
            return
        }

        if (holdActivation = "normal") {
            if (waitEnabled && waitButton != "") {
                if !GetKeyState(waitButton, "P")
                    return
            }
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
        GuiControl,, StatusText, Turbo Mode ON
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

    if (holdActivation = "normal") {
        if (waitEnabled && waitButton != "") {
            if !GetKeyState(waitButton, "P")
                return
        }
    }

    if (A_TickCount - holdStart >= holdDelay && !turboMode) {
        turboMode := true
        GuiControl,, StatusText, Turbo Mode ON
        SetTimer, TurboClick, % (1000 / turboCPS)
        SetTimer, HoldCheck, Off
    }
return

~*LButton Up::
    if (!universalEnabled)
        return

    if (mode = "hold") {
        turboMode := false
        GuiControl,, StatusText, Idle
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
        GuiControl,, StatusText, Idle
        SetTimer, TurboClick, Off
        SetTimer, StopTurbo, Off
    }
return

TurboClick:
    if (!universalEnabled)
        return

    if turboMode && GetKeyState("LButton", "P")
        Click
return

SaveSettings:
    Gui, Submit, NoHide
    mode := ModeChoice
    triggerCPS := TriggerCPS
    turboCPS := TurboCPS
    stopDelay := StopDelay
    holdDelay := HoldDelay
    dblInterval := DblInterval
    holdActivation := HoldActivation
    waitEnabled := WaitEnabled
    universalEnabled := UniversalEnabled
    GuiControl,, StatusText, Settings Updated
    AutoSave()
    SetTimer, FadeStatus, -700
return

AutoSave() {
    global currentProfile
    SaveProfile(currentProfile)
}

FadeStatus:
    GuiControl,, StatusText, Idle
return

GuiClose:
ExitApp
