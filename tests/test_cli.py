"""Unit tests for the cloakbrowser CLI diagnostics (`info` / `doctor`)."""

import json
import sys
from argparse import Namespace
from unittest.mock import patch

import pytest

from cloakbrowser.__main__ import _binary_version, cmd_info
from cloakbrowser.license import LicenseInfo


def _run(args, *, key=None, license_info=None):
    """Run cmd_info with license resolution mocked and the real downloaders patched.

    key=None  -> no license -> free binary.
    key set   -> validate_license returns license_info (entitled to Pro if valid).

    Returns (download_free_mock, download_pro_mock) so callers can assert the
    command never triggers a binary download.
    """
    with (
        patch("cloakbrowser.license.resolve_license_key", return_value=key),
        patch("cloakbrowser.license.validate_license", return_value=license_info),
        patch("cloakbrowser.download._download_and_extract") as mock_dl_free,
        patch("cloakbrowser.download._download_pro_binary") as mock_dl_pro,
    ):
        cmd_info(args)
    return mock_dl_free, mock_dl_pro


def test_info_text_never_downloads(capsys):
    free_dl, pro_dl = _run(Namespace(quick=True, json=False))
    free_dl.assert_not_called()
    pro_dl.assert_not_called()
    out = capsys.readouterr().out
    assert "CloakBrowser diagnostics" in out
    assert "Python:" in out
    assert "Platform:" in out
    assert "License:   Free" in out
    assert "Modules:" in out


def test_info_quick_skips_launch(capsys):
    _run(Namespace(quick=True, json=False))
    out = capsys.readouterr().out
    assert "skipped (--quick)" in out


def test_keyless_reports_free_binary(capsys):
    """No license key -> the binary section reflects the FREE binary."""
    _run(Namespace(quick=True, json=True))
    data = json.loads(capsys.readouterr().out)
    assert data["binary"]["tier"] == "free"
    assert data["license"]["tier"] == "free"


def test_valid_key_reports_pro_binary(capsys):
    """A server-validated key -> the binary section reflects the PRO binary."""
    valid = LicenseInfo(valid=True, plan="business", expires=None)
    with patch("cloakbrowser.license.get_pro_latest_version", return_value="148.0.0.0"):
        _run(Namespace(quick=True, json=True), key="cb_test", license_info=valid)
    data = json.loads(capsys.readouterr().out)
    assert data["binary"]["tier"] == "pro"
    assert data["binary"]["version"] == "148.0.0.0"
    assert data["license"]["tier"] == "business"


def test_invalid_key_falls_back_to_free(capsys):
    """A key the server rejects -> not entitled -> free binary, not Pro."""
    invalid = LicenseInfo(valid=False, plan="solo", expires=None)
    _run(Namespace(quick=True, json=True), key="cb_bad", license_info=invalid)
    data = json.loads(capsys.readouterr().out)
    assert data["binary"]["tier"] == "free"
    assert data["license"]["tier"] == "invalid"


def test_info_json_is_valid(capsys):
    _run(Namespace(quick=True, json=True))
    out = capsys.readouterr().out
    data = json.loads(out)
    assert data["environment"]["python"]
    assert "modules" in data
    # Upgrade hint is text-only; JSON exposes tier as a field instead.
    assert "cloakbrowser.dev" not in out


def test_free_license_shows_upgrade_hint(capsys):
    _run(Namespace(quick=True, json=False))
    out = capsys.readouterr().out
    assert "License:   Free" in out
    assert "Add a license key" in out


# ---------------------------------------------------------------------------
# Launch test — exercises the real subprocess path (not --quick) against a stub
# executable, so the launch-test code is actually covered by CI.
# ---------------------------------------------------------------------------

pytestmark_posix = pytest.mark.skipif(
    sys.platform == "win32", reason="uses a POSIX shell stub binary"
)


@pytestmark_posix
def test_binary_version_runs_stub(tmp_path):
    stub = tmp_path / "fakechrome"
    stub.write_text("#!/bin/sh\necho 'Chromium 1.2.3.4'\n")
    stub.chmod(0o755)
    ok, version, err = _binary_version(str(stub))
    assert ok
    assert "Chromium 1.2.3.4" in version
    assert err == ""


@pytestmark_posix
def test_binary_version_reports_failure(tmp_path):
    stub = tmp_path / "failchrome"
    stub.write_text("#!/bin/sh\necho 'libfoo missing' >&2\nexit 1\n")
    stub.chmod(0o755)
    ok, version, err = _binary_version(str(stub))
    assert not ok
    assert "libfoo missing" in err


@pytestmark_posix
def test_launch_section_runs_binary_without_downloading(tmp_path, capsys):
    """Full non-quick run: the launch test executes the resolved binary, and no
    download function is ever invoked."""
    stub = tmp_path / "fakechrome"
    stub.write_text("#!/bin/sh\necho 'Chromium 9.9.9.9'\n")
    stub.chmod(0o755)
    fake_binary = {
        "version": "9.9.9.9",
        "tier": "free",
        "bundled_version": "x",
        "path": str(stub),
        "installed": True,
        "cache_dir": str(tmp_path),
        "override": None,
    }
    with (
        patch("cloakbrowser.license.resolve_license_key", return_value=None),
        patch("cloakbrowser.__main__._effective_binary", return_value=fake_binary),
        patch("cloakbrowser.download._download_and_extract") as mock_dl_free,
        patch("cloakbrowser.download._download_pro_binary") as mock_dl_pro,
    ):
        cmd_info(Namespace(quick=False, json=True))
    data = json.loads(capsys.readouterr().out)
    assert data["launch"]["tested"] is True
    assert data["launch"]["ok"] is True
    assert "Chromium 9.9.9.9" in data["launch"]["version"]
    mock_dl_free.assert_not_called()
    mock_dl_pro.assert_not_called()
