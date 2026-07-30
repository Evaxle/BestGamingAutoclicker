from __future__ import annotations

import json
import shutil
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional


@dataclass
class AppConfig:
    profile_name: str = "default"
    mode: str = "spam"
    trigger_cps: int = 5
    turbo_cps: int = 70
    stop_delay: int = 1000
    hold_delay: int = 200
    dbl_interval: int = 300
    hold_activation: str = "normal"
    wait_button: str = ""
    wait_enabled: bool = False
    universal_enabled: bool = False
    auto_save_interval: int = 60  # seconds, 10-3600 range


DEFAULT_CONFIG = AppConfig()


def _coerce_int(value: object, fallback: int, minimum: int = 1) -> int:
    try:
        parsed = int(value)
    except (TypeError, ValueError):
        return fallback
    return max(minimum, parsed)


def _coerce_bool(value: object) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return bool(value)
    if isinstance(value, str):
        lowered = value.strip().lower()
        if lowered in {"1", "true", "yes", "on"}:
            return True
        if lowered in {"0", "false", "no", "off", ""}:
            return False
    return False


def validate_settings(config: AppConfig) -> List[str]:
    errors: List[str] = []
    if config.mode not in {"spam", "hold"}:
        errors.append("mode")
    if config.trigger_cps < 1 or config.trigger_cps > 200:
        errors.append("trigger_cps")
    if config.turbo_cps < 1 or config.turbo_cps > 500:
        errors.append("turbo_cps")
    if config.stop_delay < 0 or config.stop_delay > 10000:
        errors.append("stop_delay")
    if config.hold_delay < 0 or config.hold_delay > 10000:
        errors.append("hold_delay")
    if config.dbl_interval < 0 or config.dbl_interval > 5000:
        errors.append("dbl_interval")
    if config.hold_activation not in {"normal", "double-click"}:
        errors.append("hold_activation")
    if config.auto_save_interval < 10 or config.auto_save_interval > 3600:
        errors.append("auto_save_interval")
    return errors


def sanitize_config(config: AppConfig) -> AppConfig:
    safe = AppConfig(
        profile_name=config.profile_name or "default",
        mode=config.mode if config.mode in {"spam", "hold"} else "spam",
        trigger_cps=_coerce_int(config.trigger_cps, 5, 1),
        turbo_cps=_coerce_int(config.turbo_cps, 70, 1),
        stop_delay=_coerce_int(config.stop_delay, 1000, 0),
        hold_delay=_coerce_int(config.hold_delay, 200, 0),
        dbl_interval=_coerce_int(config.dbl_interval, 300, 0),
        hold_activation=config.hold_activation if config.hold_activation in {"normal", "double-click"} else "normal",
        wait_button=(config.wait_button or "").strip(),
        wait_enabled=_coerce_bool(config.wait_enabled),
        universal_enabled=_coerce_bool(config.universal_enabled),
        auto_save_interval=_coerce_int(config.auto_save_interval, 60, 10),
    )
    if safe.mode == "hold" and safe.hold_activation == "double-click":
        safe.wait_enabled = False
    # Clamp auto_save_interval to valid range
    safe.auto_save_interval = max(10, min(3600, safe.auto_save_interval))
    return safe


def load_config(profile_path: Path | str) -> AppConfig:
    path = Path(profile_path)
    if not path.exists():
        return sanitize_config(DEFAULT_CONFIG)

    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return sanitize_config(DEFAULT_CONFIG)

    settings = payload.get("Settings", {}) if isinstance(payload, dict) else {}
    config = AppConfig(
        profile_name=path.stem,
        mode=settings.get("mode", DEFAULT_CONFIG.mode),
        trigger_cps=int(settings.get("trigger_cps", DEFAULT_CONFIG.trigger_cps)),
        turbo_cps=int(settings.get("turbo_cps", DEFAULT_CONFIG.turbo_cps)),
        stop_delay=int(settings.get("stop_delay", DEFAULT_CONFIG.stop_delay)),
        hold_delay=int(settings.get("hold_delay", DEFAULT_CONFIG.hold_delay)),
        dbl_interval=int(settings.get("dbl_interval", DEFAULT_CONFIG.dbl_interval)),
        hold_activation=settings.get("hold_activation", DEFAULT_CONFIG.hold_activation),
        wait_button=settings.get("wait_button", ""),
        wait_enabled=_coerce_bool(settings.get("wait_enabled", False)),
        universal_enabled=_coerce_bool(settings.get("universal_enabled", False)),
        auto_save_interval=int(settings.get("auto_save_interval", DEFAULT_CONFIG.auto_save_interval)),
    )
    return sanitize_config(config)


def save_config(profile_path: Path | str, config: AppConfig) -> None:
    path = Path(profile_path)
    path.parent.mkdir(parents=True, exist_ok=True)

    # Create backup of existing profile before overwriting
    if path.exists():
        _backup_profile(path)

    safe_config = sanitize_config(config)
    payload = {
        "Settings": {
            "mode": safe_config.mode,
            "trigger_cps": str(safe_config.trigger_cps),
            "turbo_cps": str(safe_config.turbo_cps),
            "stop_delay": str(safe_config.stop_delay),
            "hold_delay": str(safe_config.hold_delay),
            "dbl_interval": str(safe_config.dbl_interval),
            "hold_activation": safe_config.hold_activation,
            "wait_button": safe_config.wait_button,
            "wait_enabled": "1" if safe_config.wait_enabled else "0",
            "universal_enabled": "1" if safe_config.universal_enabled else "0",
            "auto_save_interval": str(safe_config.auto_save_interval),
        }
    }
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)


def _backup_profile(profile_path: Path) -> Optional[Path]:
    """Create a timestamped backup of a profile JSON file."""
    try:
        backup_dir = profile_path.parent / "backups"
        backup_dir.mkdir(parents=True, exist_ok=True)
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        backup_name = f"{profile_path.stem}_backup_{timestamp}.json"
        backup_path = backup_dir / backup_name
        shutil.copy2(profile_path, backup_path)
        # Clean old backups (keep last 5 per profile)
        _clean_old_backups(backup_dir, profile_path.stem, max_keep=5)
        return backup_path
    except (OSError, shutil.Error):
        return None


def _clean_old_backups(backup_dir: Path, profile_stem: str, max_keep: int = 5) -> None:
    """Remove older backups for a profile, keeping only the latest `max_keep`."""
    backups = sorted(backup_dir.glob(f"{profile_stem}_backup_*.json"), reverse=True)
    for old_backup in backups[max_keep:]:
        try:
            old_backup.unlink()
        except OSError:
            pass


def save_runtime_state(data_dir: Path | str, *, enabled: bool, profile_name: str) -> None:
    directory = Path(data_dir)
    directory.mkdir(parents=True, exist_ok=True)
    state_path = directory / "runtime_state.json"
    payload = {
        "Runtime": {
            "enabled": "1" if enabled else "0",
            "profile_name": profile_name or "default",
        }
    }
    with state_path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)


def load_runtime_state(data_dir: Path | str) -> Dict[str, object]:
    directory = Path(data_dir)
    state_path = directory / "runtime_state.json"
    if not state_path.exists():
        return {"enabled": False, "profile_name": "default"}

    try:
        payload = json.loads(state_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {"enabled": False, "profile_name": "default"}

    runtime = payload.get("Runtime", {}) if isinstance(payload, dict) else {}
    return {
        "enabled": _coerce_bool(runtime.get("enabled", False)),
        "profile_name": runtime.get("profile_name", "default"),
    }


def list_profiles(data_dir: Path | str) -> List[str]:
    directory = Path(data_dir)
    if not directory.exists():
        return ["default"]

    profiles: List[str] = []
    for path in sorted(directory.glob("*.json")):
        stem = path.stem
        if stem in {"default", "runtime_state"}:
            continue
        profiles.append(stem)
    return ["default", *profiles]


def profile_path(data_dir: Path | str, profile_name: str) -> Path:
    directory = Path(data_dir)
    directory.mkdir(parents=True, exist_ok=True)
    return directory / f"{profile_name}.json"
