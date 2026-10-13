#!/usr/bin/env python3
"""Info-hash matching for the libtorrent sidecar.

v2-only torrents are stored as a 64-hex btmh hash. The daemon must find and
report that hash. The truncated sha1 on torrent_status.info_hash is not it.
"""

import importlib.util
import sys
import types
import unittest
from pathlib import Path

V1_HASH = "0123456789abcdef0123456789abcdef01234567"
V2_HASH = "d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c"
V2_TRUNCATED = V2_HASH[:40]
OTHER_V1 = "abcdef0123456789abcdef0123456789abcdef01"


def _install_libtorrent_stub():
    lt = types.ModuleType("libtorrent")

    class _AlertCategory:
        error_notification = 1
        status_notification = 2

    class _Alert:
        category_t = _AlertCategory

    class _TorrentFlags:
        paused = 1

    class _RemoveFlags:
        delete_files = 1

    lt.alert = _Alert
    lt.torrent_flags = _TorrentFlags
    lt.remove_flags_t = _RemoveFlags
    lt.session = lambda settings=None: None
    lt.__version__ = "2.1.1-test"
    sys.modules["libtorrent"] = lt


def _load_daemon():
    _install_libtorrent_stub()
    path = Path(__file__).with_name("libtorrent_daemon.py")
    spec = importlib.util.spec_from_file_location("libtorrent_daemon_under_test", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FakeHash:
    def __init__(self, text):
        self.text = text

    def __str__(self):
        return self.text


class FakeInfoHashes:
    def __init__(self, v1=None, v2=None):
        self.v1 = FakeHash(v1 or "")
        self.v2 = FakeHash(v2 or "")
        self._v1 = v1
        self._v2 = v2

    def has_v1(self):
        return bool(self._v1)

    def has_v2(self):
        return bool(self._v2)


class FakeStatus:
    def __init__(self, v1=None, v2=None, legacy=None, include_info_hashes=True, progress=0.4):
        if include_info_hashes:
            self.info_hashes = FakeInfoHashes(v1, v2)
        self.info_hash = FakeHash(legacy if legacy is not None else (v1 or ""))
        self.progress = progress
        self.download_rate = 10
        self.upload_rate = 2
        self.total_done = 100
        self.total_upload = 5
        self.state = "downloading"
        self.paused = False
        self.num_seeds = 1
        self.num_peers = 3
        self.super_seeding = False


class FakeHandle:
    def __init__(self, status):
        self._status = status
        self.valid = True
        self.was_paused = False
        self.auto_managed_value = None
        self.removed = False
        self.remove_flags = None

    def is_valid(self):
        return self.valid

    def status(self):
        return self._status

    def info_hash(self):
        return self._status.info_hash

    def has_metadata(self):
        return False

    def get_peer_info(self):
        return []

    def piece_availability(self):
        return []

    def auto_managed(self, value):
        self.auto_managed_value = value

    def pause(self):
        self.was_paused = True


class FakeSession:
    def __init__(self, torrents):
        self.torrents = list(torrents)
        self.removed = []

    def get_torrents(self):
        return list(self.torrents)

    def remove_torrent(self, handle, flags):
        handle.removed = True
        handle.remove_flags = flags
        self.removed.append(handle)
        if handle in self.torrents:
            self.torrents.remove(handle)


class LibTorrentInfoHashTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.daemon = _load_daemon()

    def _manager(self, handles):
        manager = self.daemon.LibTorrentManager.__new__(self.daemon.LibTorrentManager)
        manager.lock = self.daemon.threading.Lock()
        manager.pending_first_last = {}
        manager.version_target = "2.1.1"
        session = FakeSession(handles)
        manager.session = session
        return manager, session

    def test_v2_only_find_handle_matches_btmh_hash(self):
        status = FakeStatus(v2=V2_HASH, legacy=V2_TRUNCATED, progress=0.55)
        handle = FakeHandle(status)
        manager, _session = self._manager([handle])

        self.assertIs(manager.find_handle(V2_HASH), handle)
        self.assertIs(manager.find_handle(V2_HASH.upper()), handle)
        self.assertIsNone(manager.find_handle(V2_TRUNCATED))
        self.assertIsNone(manager.find_handle(V1_HASH))

    def test_v2_only_status_reports_btmh_hash_and_progress(self):
        status = FakeStatus(v2=V2_HASH, legacy=V2_TRUNCATED, progress=0.55)
        handle = FakeHandle(status)
        manager, _session = self._manager([handle])

        torrents = manager.get_torrents_status()

        self.assertEqual(len(torrents), 1)
        self.assertEqual(torrents[0]["info_hash"], V2_HASH)
        self.assertEqual(torrents[0]["progress"], 0.55)

    def test_v2_only_remove_and_pause_reach_the_session(self):
        status = FakeStatus(v2=V2_HASH, legacy=V2_TRUNCATED)
        handle = FakeHandle(status)
        manager, session = self._manager([handle])

        paused = manager.pause_torrent({"info_hash": V2_HASH})
        self.assertEqual(paused["status"], "paused")
        self.assertTrue(handle.was_paused)
        self.assertIs(handle.auto_managed_value, False)

        removed = manager.remove_torrent({"info_hash": V2_HASH, "delete_files": True})
        self.assertEqual(removed["status"], "removed")
        self.assertTrue(handle.removed)
        self.assertEqual(handle.remove_flags, self.daemon.lt.remove_flags_t.delete_files)
        self.assertEqual(session.removed, [handle])

    def test_v1_hash_still_matches_and_is_reported(self):
        status = FakeStatus(v1=V1_HASH, legacy=V1_HASH, progress=0.2)
        handle = FakeHandle(status)
        manager, _session = self._manager([handle])

        self.assertIs(manager.find_handle(V1_HASH), handle)
        torrents = manager.get_torrents_status()
        self.assertEqual(torrents[0]["info_hash"], V1_HASH)
        self.assertEqual(torrents[0]["progress"], 0.2)

    def test_hybrid_reports_v1_and_matches_either_hash(self):
        status = FakeStatus(v1=V1_HASH, v2=V2_HASH, legacy=V1_HASH)
        handle = FakeHandle(status)
        other = FakeHandle(FakeStatus(v1=OTHER_V1, legacy=OTHER_V1))
        manager, _session = self._manager([other, handle])

        self.assertIs(manager.find_handle(V1_HASH), handle)
        self.assertIs(manager.find_handle(V2_HASH), handle)
        self.assertIs(manager.find_handle(OTHER_V1), other)
        reported = {item["info_hash"] for item in manager.get_torrents_status()}
        self.assertEqual(reported, {V1_HASH, OTHER_V1})

    def test_legacy_status_without_info_hashes_uses_info_hash(self):
        status = FakeStatus(legacy=V1_HASH, include_info_hashes=False)
        handle = FakeHandle(status)
        manager, _session = self._manager([handle])

        self.assertIs(manager.find_handle(V1_HASH), handle)
        self.assertEqual(manager.get_torrents_status()[0]["info_hash"], V1_HASH)

    def test_primary_hash_prefers_v1_then_v2(self):
        hybrid = FakeStatus(v1=V1_HASH, v2=V2_HASH, legacy=V1_HASH)
        v2_only = FakeStatus(v2=V2_HASH, legacy=V2_TRUNCATED)
        self.assertEqual(self.daemon._primary_info_hash(hybrid), V1_HASH)
        self.assertEqual(self.daemon._primary_info_hash(v2_only), V2_HASH)
        self.assertEqual(
            self.daemon._collect_info_hash_strings(v2_only),
            [V2_HASH],
        )


if __name__ == "__main__":
    unittest.main()
