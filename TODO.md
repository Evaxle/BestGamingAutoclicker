# ✅ Task List - Advanced Settings, Auto-Save Interval & Profile Loading Fix

## Phase 1: Config Layer Updates
- [ ] Add `auto_save_interval` field to `AppConfig` dataclass (default: 60 seconds)
- [ ] Add validation for `auto_save_interval` (range: 10-3600)
- [ ] Add coercion/sanitization for `auto_save_interval`
- [ ] Update `save_config()` to persist `auto_save_interval`
- [ ] Update `load_config()` to read `auto_save_interval`

## Phase 2: GUI - Advanced Settings Toggle
- [ ] Create "Advanced Settings" toggle button in settings section
- [ ] Create collapsible frame for advanced settings (hidden by default)
- [ ] Move console log widget into advanced settings frame
- [ ] Add auto-save interval input field in advanced settings

## Phase 3: GUI - Wire Up New Field
- [ ] Update `_apply_config_to_form()` to set auto-save interval field
- [ ] Update `_collect_config()` to read auto-save interval
- [ ] Verify profile loading populates ALL fields correctly including new one

## Phase 4: Verification
- [ ] Run Python syntax check on gui.py and autoclicker_config.py
- [ ] Run unittest suite to verify config layer
- [ ] Verify all form fields populate on profile load

