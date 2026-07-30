# Comprehensive Plan: Fix Autoclicker, Add Console Log & Safety Checks

## Issues Identified

### Autoclicker Engine Bugs:
1. **Spam mode wrong rate**: Uses `max(trigger_cps, turbo_cps)` instead of just `trigger_cps`. `stop_delay` ignored.
2. **Hold mode timing drift**: Resets timer on each click causing drift.
3. **Double-click mode broken**: Edge detection logic doesn't properly detect press events.
4. **No thread safety**: `_clicking` boolean used across threads without atomic operations.
5. **No runaway protection**: The loop doesn't verify user intent mid-cycle — if a button gets stuck, clicker runs forever.

### Missing Features:
6. **No console/debug log**: Users can't see file operations, clicker status, errors.
7. **No file operation visibility**: Profile create/delete/save happens silently.

## Plan

### Step 1: Add ConsoleLog widget to gui.py
- Scrolling text widget at bottom of content area.
- `[HH:MM:SS] [TYPE] message` with colors: INFO=grey, SUCCESS=green, WARNING=yellow, ERROR=red, FILE=purple, CLICKER=cyan.
- Auto-scroll, "Clear Log" button.

### Step 2: Fix Autoclicker Engine
- **Spam**: Click at `trigger_cps` rate. Each loop iteration checks `_clicking` (threading.Event). 
- **Hold (normal)**: Click every `hold_delay` ms while button held. In each cycle, check multiple times if button is still held AND `_clicking` is still True.
- **Hold (double-click)**: On press detection, fire two clicks. Multiple mid-cycle checks.
- **Thread safety**: Use `threading.Event` for `_clicking`. Set a max idle timeout safety (e.g., if no input detected for 60s, auto-stop).
- **Runaway protection**: In both spam and hold loops, verify `_clicking.is_set()` and button state multiple times per cycle. If left button released in hold mode, immediately break.

### Step 3: Multiple mid-cycle safety checks
- In `_python_click_loop`, check `_clicking.is_set()` at the START of each iteration AND after each sleep.
- In hold mode, check `_left_button_down` and `_wait_requirements_met()` multiple times during the delay.
- If `_left_button_down` becomes False mid-cycle in hold mode, immediately exit the hold logic.

### Step 4: Hook logging into all operations
- Profile CRUD: log file paths.
- Save: log file path.
- Clicker start/stop: log mode, CPS.
- Auto-stop events: log reason.
- Validation errors: log warnings.

### Step 5: Update TODO.md and verify

## Files to Edit
1. **gui.py** — ConsoleLog, engine fixes, safety checks, logging hooks
2. **TODO.md** — Update

