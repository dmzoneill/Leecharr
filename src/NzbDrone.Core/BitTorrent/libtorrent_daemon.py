#!/usr/bin/env python3
"""
Leecharr LibTorrent Daemon (Rasterbar C++ / Python Sidecar)
Provides an embedded HTTP JSON-RPC bridge for LibTorrentDownloadEngine on port 58846.
"""

import argparse
import base64
import json
import math
import signal
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

try:
    import libtorrent as lt
except ImportError:
    sys.stderr.write("ERROR: python3-libtorrent is not installed.\n")
    sys.exit(1)

# peer_info.choked / interesting / etc. are peer_flags_t bit masks, not live booleans.
_UTP_SOCKET_FLAG = 1 << 17


def _peer_flags_int(peer_info):
    try:
        return int(peer_info.flags)
    except (TypeError, ValueError, AttributeError):
        return 0


def _peer_has_flag(peer_info, flag_attr):
    mask = getattr(peer_info, flag_attr, 0)
    try:
        mask = int(mask)
    except (TypeError, ValueError):
        return False
    if mask <= 0:
        return False
    return bool(_peer_flags_int(peer_info) & mask)


def _peer_is_utp(peer_info):
    return bool(_peer_flags_int(peer_info) & _UTP_SOCKET_FLAG)


def _normalize_rel_path(path):
    return (path or "").replace("\\", "/").lstrip("/")


def _file_index_for_path(files, rel_path):
    target = _normalize_rel_path(rel_path).lower()
    for i in range(files.num_files()):
        if _normalize_rel_path(files.file_path(i)).lower() == target:
            return i
    return -1


def _apply_first_last_piece_priority(handle, enabled):
    if not handle.is_valid() or not handle.has_metadata():
        return False

    ti = handle.torrent_file()
    piece_count = ti.num_pieces()
    if piece_count <= 0:
        return False

    head_count = max(1, min(4, piece_count // 10))
    tail_count = max(1, min(2, piece_count // 20))
    priority = 7 if enabled else 4

    indices = set()
    for i in range(min(head_count, piece_count)):
        indices.add(i)
    for i in range(max(0, piece_count - tail_count), piece_count):
        indices.add(i)

    files = ti.files()
    for fi in range(files.num_files()):
        start = files.piece_index_at_file(fi)
        if 0 <= start < piece_count:
            indices.add(start)
        offset = files.file_offset(fi)
        size = files.file_size(fi)
        if size > 0:
            end_piece = files.piece_index_at_offset(offset + size - 1)
            if 0 <= end_piece < piece_count:
                indices.add(end_piece)

    for piece_index in indices:
        handle.piece_priority(piece_index, priority)

    return True


def _coerce_bool(value):
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        return value.strip().lower() in ("1", "true", "yes", "on")
    if isinstance(value, (int, float)):
        return value != 0
    return bool(value)


def _status_is_paused(status):
    # Since libtorrent 1.2, paused is not a torrent_status.state value.
    try:
        paused = getattr(status, "paused", None)
        if isinstance(paused, bool):
            return paused
    except Exception:  # noqa: BLE001, S110
        pass

    try:
        flags = int(getattr(status, "flags", 0))
        paused_flag = int(lt.torrent_flags.paused)
        return bool(flags & paused_flag)
    except Exception:  # noqa: BLE001
        return False


def announce_settings_from_params(params):
    if not isinstance(params, dict):
        return {}

    settings = {}
    if "announce_to_all_tiers" in params:
        settings["announce_to_all_tiers"] = _coerce_bool(params.get("announce_to_all_tiers"))
    if "announce_to_all_trackers" in params:
        settings["announce_to_all_trackers"] = _coerce_bool(params.get("announce_to_all_trackers"))
    return settings


def _append_info_hash(hashes, value):
    if value is None:
        return
    text = str(value).strip()
    if text and text not in hashes:
        hashes.append(text)


def _collect_info_hash_strings(status, handle=None):
    """Return v1 and v2 info hashes.

    Fall back to the legacy hash only when neither versioned hash is set.
    A v2-only torrent exposes a truncated sha1 through info_hash. That value is
    not the 64-hex btmh hash Leecharr stores, so it must not replace info_hashes.v2.
    """
    hashes = []
    info_hashes = getattr(status, "info_hashes", None)
    if info_hashes is not None:
        if hasattr(info_hashes, "has_v1") and info_hashes.has_v1():
            _append_info_hash(hashes, info_hashes.v1)
        if hasattr(info_hashes, "has_v2") and info_hashes.has_v2():
            _append_info_hash(hashes, info_hashes.v2)
    if hashes:
        return hashes

    if hasattr(status, "info_hash"):
        _append_info_hash(hashes, status.info_hash)
    elif handle is not None:
        _append_info_hash(hashes, handle.info_hash())
    return hashes


def _primary_info_hash(status, handle=None):
    hashes = _collect_info_hash_strings(status, handle)
    for item in hashes:
        if len(item) == 40:
            return item
    if hashes:
        return hashes[0]
    return ""


class LibTorrentManager:
    def __init__(self, listen_interfaces="0.0.0.0:6882", version_target="2.1.1"):
        self.lock = threading.Lock()
        self.version_target = version_target
        settings = {
            "listen_interfaces": listen_interfaces,
            "alert_mask": lt.alert.category_t.error_notification | lt.alert.category_t.status_notification,
            # Off until set_settings. Otherwise the session announces on the
            # clearnet before a configured proxy can be applied.
            "enable_dht": False,
            "enable_lsd": True,
            "enable_upnp": True,
            "enable_natpmp": True,
        }
        self.session = lt.session(settings)
        self.pending_first_last = {}

    def find_handle(self, info_hash):
        if not info_hash:
            return None
        target = info_hash.lower()
        with self.lock:
            for h in self.session.get_torrents():
                if not h.is_valid():
                    continue
                st = h.status()
                for candidate in _collect_info_hash_strings(st, h):
                    if candidate.lower() == target:
                        return h
        return None

    def get_session_status(self):
        with self.lock:
            st = self.session.status()
            return {
                "status": "ok",
                "version": getattr(lt, "__version__", self.version_target),
                "dht_nodes": getattr(st, "dht_nodes", 0),
                "download_rate": getattr(st, "download_rate", 0),
                "upload_rate": getattr(st, "upload_rate", 0),
                "num_peers": getattr(st, "num_peers", 0),
            }

    def add_torrent(self, params):
        atp = None
        if params.get("ti"):
            raw = base64.b64decode(params["ti"])
            ti = lt.torrent_info(raw)
            atp = lt.add_torrent_params()
            atp.ti = ti
        elif params.get("url"):
            atp = lt.parse_magnet_uri(params["url"])
        elif params.get("info_hash"):
            atp = lt.add_torrent_params()
            atp.info_hash = lt.sha1_hash(bytes.fromhex(params["info_hash"]))
        else:
            raise ValueError("Missing torrent info or magnet url")

        if params.get("save_path"):
            atp.save_path = params["save_path"]
        if params.get("name"):
            atp.name = params["name"]

        ih_str = params.get("info_hash")
        existing = self.find_handle(ih_str) if ih_str else None
        if existing:
            with self.lock:
                if "sequential_download" in params:
                    existing.set_sequential_download(bool(params["sequential_download"]))
                if "first_last_piece_priority" in params:
                    enabled = bool(params["first_last_piece_priority"])
                    if not _apply_first_last_piece_priority(existing, enabled):
                        self.pending_first_last[ih_str.lower()] = enabled
                if "super_seeding" in params:
                    self._set_super_seeding_on_handle(existing, bool(params["super_seeding"]))
            return {"status": "already_added", "info_hash": ih_str}

        with self.lock:
            h = self.session.add_torrent(atp)
            trackers = params.get("trackers")
            if trackers and isinstance(trackers, list):
                for tr in trackers:
                    try:
                        h.add_tracker({"url": str(tr), "tier": 0})
                    except Exception:  # noqa: BLE001, S110
                        pass
            try:
                if hasattr(h, "auto_managed"):
                    h.auto_managed(True)
                if hasattr(h, "resume"):
                    h.resume()
            except Exception:  # noqa: BLE001, S110
                pass

            if "sequential_download" in params:
                try:
                    h.set_sequential_download(bool(params["sequential_download"]))
                except Exception:  # noqa: BLE001, S110
                    pass
            if "first_last_piece_priority" in params:
                enabled = bool(params["first_last_piece_priority"])
                if not _apply_first_last_piece_priority(h, enabled) and ih_str:
                    self.pending_first_last[ih_str.lower()] = enabled
            if "super_seeding" in params:
                self._set_super_seeding_on_handle(h, bool(params["super_seeding"]))

            st = h.status()
            ih = _primary_info_hash(st, h)
            return {"status": "added", "info_hash": ih}

    def remove_torrent(self, params):
        h = self.find_handle(params.get("info_hash"))
        if not h:
            return {"status": "not_found"}
        delete_files = bool(params.get("delete_files", False))
        flags = (
            lt.remove_flags_t.delete_files
            if delete_files and hasattr(lt, "remove_flags_t")
            else (1 if delete_files else 0)
        )
        with self.lock:
            self.session.remove_torrent(h, flags)
        return {"status": "removed"}

    def pause_torrent(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            with self.lock:
                # An auto-managed torrent is resumed by the session after pause().
                try:
                    if hasattr(h, "auto_managed"):
                        h.auto_managed(False)
                except Exception:  # noqa: BLE001, S110
                    pass
                h.pause()
            return {"status": "paused"}
        return {"status": "not_found"}

    def resume_torrent(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            with self.lock:
                try:
                    if hasattr(h, "auto_managed"):
                        h.auto_managed(True)
                    if hasattr(h, "resume"):
                        h.resume()
                except Exception:  # noqa: BLE001, S110
                    pass
            return {"status": "resumed"}
        return {"status": "not_found"}

    def pause_session(self):
        with self.lock:
            self.session.pause()
        return {"status": "session_paused"}

    def resume_session(self):
        with self.lock:
            self.session.resume()
        return {"status": "session_resumed"}

    def rebind_network_interfaces(self, params):
        interfaces = params.get("interfaces", "0.0.0.0:6882")
        with self.lock:
            self.session.apply_settings({"listen_interfaces": interfaces})
        return {"status": "rebound", "interfaces": interfaces}

    def force_recheck(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            with self.lock:
                h.force_recheck()
            return {"status": "rechecking"}
        return {"status": "not_found"}

    def force_reannounce(self, params):
        h = self.find_handle(params.get("info_hash"))
        if not h:
            return {"status": "not_found"}

        announce_settings = announce_settings_from_params(params)
        with self.lock:
            if announce_settings:
                try:
                    self.session.apply_settings(announce_settings)
                except Exception:  # noqa: BLE001, S110
                    pass

            # tracker_idx -1 schedules every tracker. libtorrent then keeps only
            # the trackers allowed by announce_to_all_tiers and
            # announce_to_all_trackers. Both default to false, so a bare
            # force_reannounce() hits only the current tracker.
            try:
                h.force_reannounce(0, -1)
            except TypeError:
                h.force_reannounce()
        return {"status": "reannounced"}

    def disconnect_peer(self, params):
        h = self.find_handle(params.get("info_hash"))
        if not h:
            return {"status": "not_found", "disconnected": 0}

        target = (params.get("ip") or "").strip()
        if not target:
            return {"status": "invalid_ip", "disconnected": 0}

        target_norm = target.lower().strip("[]")
        disconnected = 0
        with self.lock:
            try:
                peers = list(h.get_peer_info())
            except Exception:  # noqa: BLE001
                peers = []

            for pi in peers:
                peer_ip = pi.ip[0] if isinstance(pi.ip, tuple) else str(pi.ip)
                if not peer_ip:
                    continue
                peer_norm = str(peer_ip).lower().strip("[]")
                if peer_norm != target_norm:
                    continue
                try:
                    endpoint = pi.endpoint
                except Exception:  # noqa: BLE001
                    port = pi.ip[1] if isinstance(pi.ip, tuple) else 0
                    try:
                        endpoint = lt.tcp_endpoint(lt.address_from_string(str(peer_ip)), int(port))
                    except Exception:  # noqa: BLE001, S112
                        continue
                try:
                    h.disconnect_peer(endpoint)
                    disconnected += 1
                except Exception:  # noqa: BLE001, S110
                    pass

        return {"status": "ok", "disconnected": disconnected}

    def add_trackers(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            trackers = params.get("trackers", [])
            with self.lock:
                for t in trackers:
                    try:
                        h.add_tracker({"url": t, "tier": 0})
                    except Exception:  # noqa: BLE001, S110
                        pass
            return {"status": "trackers_added"}
        return {"status": "not_found"}

    def remove_trackers(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            to_remove = set(params.get("trackers", []))
            with self.lock:
                current = h.trackers()
                updated = [t for t in current if t.get("url") not in to_remove]
                h.replace_trackers(updated)
            return {"status": "trackers_removed"}
        return {"status": "not_found"}

    def set_file_priorities(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            with self.lock:
                prio = int(params.get("priority", 1))
                file_path = params.get("file_path")
                if file_path and h.has_metadata():
                    ti = h.torrent_file()
                    files = ti.files()
                    for i in range(files.num_files()):
                        if files.file_path(i) == file_path:
                            h.file_priority(i, prio)
                            break
            return {"status": "priorities_set"}
        return {"status": "not_found"}

    def set_sequential_download(self, params):
        h = self.find_handle(params.get("info_hash"))
        if not h:
            return {"status": "not_found"}
        enabled = bool(params.get("enabled", False))
        with self.lock:
            h.set_sequential_download(enabled)
        return {"status": "sequential_set", "enabled": enabled}

    def set_first_last_piece_priority(self, params):
        ih = (params.get("info_hash") or "").lower()
        h = self.find_handle(params.get("info_hash"))
        if not h:
            return {"status": "not_found"}
        enabled = bool(params.get("enabled", False))
        with self.lock:
            if _apply_first_last_piece_priority(h, enabled):
                self.pending_first_last.pop(ih, None)
                return {"status": "priorities_set", "enabled": enabled}
            self.pending_first_last[ih] = enabled
        return {"status": "pending_metadata", "enabled": enabled}

    def set_settings(self, params):
        settings = {}
        if "download_rate_limit" in params:
            settings["download_rate_limit"] = int(params["download_rate_limit"])
        if "upload_rate_limit" in params:
            settings["upload_rate_limit"] = int(params["upload_rate_limit"])

        # Proxy configuration
        proxy_type = params.get("proxy_type")
        if proxy_type is not None:
            if isinstance(proxy_type, str):
                p_map = {
                    "none": 0,
                    "disabled": 0,
                    "socks4": 1,
                    "socks5": 2,
                    "socks5_pw": 3,
                    "http": 4,
                    "http_pw": 5,
                    "https": 4,
                    "i2p": 6,
                    "i2p_proxy": 6,
                }
                pt = p_map.get(proxy_type.strip().lower(), 0)
                has_auth = bool(
                    params.get("proxy_username")
                    or params.get("proxy_password")
                    or params.get("proxy_auth_username")
                    or params.get("proxy_auth_password")
                )
                if has_auth:
                    if pt == 2:
                        pt = 3
                    elif pt == 4:
                        pt = 5
                settings["proxy_type"] = pt
            else:
                settings["proxy_type"] = int(proxy_type)

        if "proxy_hostname" in params:
            settings["proxy_hostname"] = str(params["proxy_hostname"])
        elif "proxy_host" in params:
            settings["proxy_hostname"] = str(params["proxy_host"])

        if "proxy_port" in params:
            settings["proxy_port"] = int(params["proxy_port"])

        if "proxy_username" in params:
            settings["proxy_username"] = str(params["proxy_username"])
        elif "proxy_auth_username" in params:
            settings["proxy_username"] = str(params["proxy_auth_username"])

        if "proxy_password" in params:
            settings["proxy_password"] = str(params["proxy_password"])
        elif "proxy_auth_password" in params:
            settings["proxy_password"] = str(params["proxy_auth_password"])

        if "force_proxy" in params:
            settings["force_proxy"] = bool(params["force_proxy"])
        if "proxy_peer_connections" in params:
            settings["proxy_peer_connections"] = bool(params["proxy_peer_connections"])
        if "proxy_tracker_connections" in params:
            settings["proxy_tracker_connections"] = bool(params["proxy_tracker_connections"])
        if "proxy_hostnames" in params:
            settings["proxy_hostnames"] = bool(params["proxy_hostnames"])

        # DHT settings
        if "enable_dht" in params:
            settings["enable_dht"] = bool(params["enable_dht"])
        elif "dht_enabled" in params:
            settings["enable_dht"] = bool(params["dht_enabled"])

        # DHT UDP leaves on the listen socket. proxy_peer_connections and
        # force_proxy do not carry it, so a configured proxy keeps DHT off.
        if settings.get("proxy_type") not in (None, 0):
            settings["enable_dht"] = False

        if "dht_bootstrap_nodes" in params:
            nodes = params["dht_bootstrap_nodes"]
            if isinstance(nodes, list):
                nodes = ",".join(str(n) for n in nodes)
            settings["dht_bootstrap_nodes"] = str(nodes)

        # Anonymous mode
        if "anonymous_mode" in params:
            settings["anonymous_mode"] = bool(params["anonymous_mode"])

        # Listening interfaces & port
        if "listen_interfaces" in params:
            settings["listen_interfaces"] = str(params["listen_interfaces"])
        elif "listening_port" in params or "listen_port" in params:
            port = int(params.get("listening_port") or params.get("listen_port"))
            listen_ip = str(params.get("listen_ip") or params.get("bind_interface") or "0.0.0.0")
            if listen_ip in ("", "Any", "all"):
                listen_ip = "0.0.0.0"
            settings["listen_interfaces"] = f"{listen_ip}:{port}"

        # Connections limit
        if "connections_limit" in params:
            settings["connections_limit"] = int(params["connections_limit"])
        elif "max_connections" in params or "max_global_connections" in params:
            settings["connections_limit"] = int(params.get("max_connections") or params.get("max_global_connections"))

        # Discovery protocols (UPnP, NAT-PMP, LSD)
        if "enable_upnp" in params:
            settings["enable_upnp"] = bool(params["enable_upnp"])
        elif "upnp_enabled" in params:
            settings["enable_upnp"] = bool(params["upnp_enabled"])

        if "enable_natpmp" in params:
            settings["enable_natpmp"] = bool(params["enable_natpmp"])

        if "enable_lsd" in params:
            settings["enable_lsd"] = bool(params["enable_lsd"])

        settings.update(announce_settings_from_params(params))

        dht_applied = self._apply_dht_settings(params)

        if settings:
            with self.lock:
                self.session.apply_settings(settings)
        if dht_applied or settings:
            return {"status": "settings_applied"}
        return {"status": "settings_applied"}

    def _apply_dht_settings(self, params):
        dht_keys = (
            "dht_routing_table_size",
            "dht_announcement_interval",
            "dht_bootstrap_timeout",
            "dht_query_timeout",
            "dht_max_nodes",
            "dht_bucket_size",
            "dht_concurrent_queries",
            "dht_auto_bootstrap",
            "dht_rate_limit_enabled",
            "dht_max_queries_per_second",
        )
        if not any(key in params for key in dht_keys):
            return False

        ds = lt.dht_settings()
        if "dht_bucket_size" in params:
            ds.max_peers_reply = max(1, int(params["dht_bucket_size"]))
        if "dht_max_nodes" in params:
            ds.max_dht_items = max(1, int(params["dht_max_nodes"]))
        if "dht_concurrent_queries" in params:
            ds.search_branching = max(1, int(params["dht_concurrent_queries"]))
        if "dht_query_timeout" in params:
            ds.block_timeout = max(1, int(params["dht_query_timeout"]))
        if "dht_bootstrap_timeout" in params:
            ds.item_lifetime = max(1, int(params["dht_bootstrap_timeout"]))
        if "dht_routing_table_size" in params:
            ds.extended_routing_table = int(params["dht_routing_table_size"]) >= 400
        if "dht_rate_limit_enabled" in params or "dht_max_queries_per_second" in params:
            enabled = bool(params.get("dht_rate_limit_enabled", True))
            if enabled:
                ds.block_ratelimit = max(1, int(params.get("dht_max_queries_per_second", 30)))
            else:
                ds.block_ratelimit = 0
        if "dht_auto_bootstrap" in params:
            ds.read_only = not bool(params["dht_auto_bootstrap"])

        with self.lock:
            self.session.set_dht_settings(ds)
        return True

    def set_torrent_limits(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            with self.lock:
                if "download_limit" in params:
                    h.set_download_limit(int(params["download_limit"]))
                if "upload_limit" in params:
                    h.set_upload_limit(int(params["upload_limit"]))
            return {"status": "limits_set"}
        return {"status": "not_found"}

    def _set_super_seeding_on_handle(self, h, enabled):
        super_flag = lt.torrent_flags.super_seeding
        if enabled:
            h.set_flags(super_flag, super_flag)
        else:
            h.unset_flags(super_flag)

    def set_super_seeding(self, params):
        h = self.find_handle(params.get("info_hash"))
        if not h:
            return {"status": "not_found"}

        enabled = bool(params.get("enabled", False))
        with self.lock:
            self._set_super_seeding_on_handle(h, enabled)
        return {"status": "super_seeding_set", "enabled": enabled}

    def set_torrent_private(self, params):
        h = self.find_handle(params.get("info_hash"))
        if not h:
            return {"status": "not_found"}

        is_private = bool(params.get("is_private", params.get("private", False)))
        bep27_flags = lt.torrent_flags.disable_dht | lt.torrent_flags.disable_pex | lt.torrent_flags.disable_lsd
        with self.lock:
            if is_private:
                h.set_flags(bep27_flags, bep27_flags)
            else:
                h.unset_flags(bep27_flags)
        return {"status": "private_set", "is_private": is_private}

    def move_storage(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            save_path = params.get("save_path")
            flags = int(params.get("flags", 0))
            with self.lock:
                h.move_storage(save_path, flags)
            return {"status": "storage_moved"}
        return {"status": "not_found"}

    def rename_file(self, params):
        h = self.find_handle(params.get("info_hash"))
        if not h:
            return {"status": "not_found"}

        old_path = params.get("old_path")
        new_path = params.get("new_path")
        if not old_path or not new_path:
            return {"status": "invalid_arguments"}

        with self.lock:
            if not h.has_metadata():
                return {"status": "not_found"}
            ti = h.torrent_file()
            files = ti.files()
            idx = _file_index_for_path(files, old_path)
            if idx < 0:
                return {"status": "file_not_found"}
            h.rename_file(idx, _normalize_rel_path(new_path))
        return {"status": "renamed"}

    def rename_folder(self, params):
        h = self.find_handle(params.get("info_hash"))
        if not h:
            return {"status": "not_found"}

        old_folder = _normalize_rel_path(params.get("old_folder")).rstrip("/")
        new_folder = _normalize_rel_path(params.get("new_folder")).rstrip("/")
        if not old_folder or not new_folder:
            return {"status": "invalid_arguments"}

        renamed = 0
        with self.lock:
            if not h.has_metadata():
                return {"status": "not_found"}
            ti = h.torrent_file()
            files = ti.files()
            prefix = old_folder.lower() + "/"
            for i in range(files.num_files()):
                fp = _normalize_rel_path(files.file_path(i))
                if not fp.lower().startswith(prefix):
                    continue
                sub_path = fp[len(old_folder) + 1 :]
                new_fp = f"{new_folder}/{sub_path}"
                h.rename_file(i, new_fp)
                renamed += 1

        if renamed == 0:
            return {"status": "file_not_found"}
        return {"status": "renamed", "renamed": renamed}

    def get_torrents_status(self):
        torrents = []
        with self.lock:
            handles = list(self.session.get_torrents())
            for h in handles:
                if not h.is_valid():
                    continue
                st = h.status()
                ih = _primary_info_hash(st, h)

                peer_list = []
                try:
                    for pi in h.get_peer_info():
                        client_raw = getattr(pi, "client", "")
                        if isinstance(client_raw, (bytes, bytearray)):
                            client_str = client_raw.decode("utf-8", errors="replace")
                        else:
                            client_str = str(client_raw) if client_raw is not None else ""

                        peer_list.append(
                            {
                                "ip": pi.ip[0] if isinstance(pi.ip, tuple) else str(pi.ip),
                                "port": pi.ip[1] if isinstance(pi.ip, tuple) else 0,
                                "client": client_str,
                                "flags": str(getattr(pi, "flags", "")),
                                "progress": float(getattr(pi, "progress", 0.0)),
                                "download_rate": int(getattr(pi, "down_speed", 0)),
                                "upload_rate": int(getattr(pi, "up_speed", 0)),
                                "total_download": int(getattr(pi, "total_download", 0)),
                                "total_upload": int(getattr(pi, "total_upload", 0)),
                                "is_encrypted": _peer_has_flag(pi, "rc4_encrypted")
                                or _peer_has_flag(pi, "plaintext_encrypted"),
                                "is_utp": _peer_is_utp(pi),
                                "is_incoming": not _peer_has_flag(pi, "local_connection"),
                                "is_choked": _peer_has_flag(pi, "remote_choked"),
                                "is_interested": _peer_has_flag(pi, "remote_interested"),
                                "client_is_choked": _peer_has_flag(pi, "choked"),
                                "client_is_interested": _peer_has_flag(pi, "interesting"),
                            }
                        )
                except Exception:  # noqa: BLE001, S110
                    pass

                ih_key = ih.lower()
                if ih_key in self.pending_first_last and h.has_metadata():
                    pending_enabled = self.pending_first_last.pop(ih_key)
                    try:
                        _apply_first_last_piece_priority(h, pending_enabled)
                    except Exception:  # noqa: BLE001
                        self.pending_first_last[ih_key] = pending_enabled

                piece_availability = []
                try:
                    piece_availability = [int(x) for x in h.piece_availability()]
                except Exception:  # noqa: BLE001, S110
                    pass

                file_paths = []
                file_progress = []
                try:
                    if h.has_metadata():
                        ti = h.torrent_file()
                        files = ti.files()
                        torrent_root = ""
                        try:
                            torrent_root = _normalize_rel_path(ti.name())
                        except Exception:  # noqa: BLE001, S110
                            pass

                        for i in range(files.num_files()):
                            file_path = _normalize_rel_path(files.file_path(i))
                            root_prefix = f"{torrent_root}/" if torrent_root else ""
                            if root_prefix and file_path.lower().startswith(root_prefix.lower()):
                                file_path = file_path[len(root_prefix) :]
                            file_paths.append(file_path)

                        file_progress = [int(x) for x in h.file_progress()]
                except Exception:  # noqa: BLE001, S110
                    pass

                state_str = str(st.state).lower()
                if "." in state_str:
                    state_str = state_str.split(".")[-1]
                paused = _status_is_paused(st)

                prog = float(getattr(st, "progress", 0.0))
                if math.isclose(prog, 0.0, abs_tol=1e-7):
                    ppm = getattr(st, "progress_ppm", 0)
                    total_wanted = getattr(st, "total_wanted", 0)
                    total_wanted_done = getattr(st, "total_wanted_done", 0)
                    if ppm > 0:
                        prog = float(ppm) / 1000000.0
                    elif total_wanted > 0 and total_wanted_done > 0:
                        prog = min(1.0, float(total_wanted_done) / float(total_wanted))
                    elif getattr(st, "total_done", 0) > 0 and getattr(st, "total_size", 0) > 0:
                        prog = min(1.0, float(st.total_done) / float(st.total_size))

                torrents.append(
                    {
                        "info_hash": ih,
                        "progress": prog,
                        "download_rate": int(st.download_rate),
                        "upload_rate": int(st.upload_rate),
                        "total_done": int(st.total_done),
                        "total_uploaded": int(st.total_upload),
                        "state": state_str,
                        "paused": paused,
                        "num_seeds": int(st.num_seeds),
                        "num_peers": int(st.num_peers),
                        "super_seeding": bool(getattr(st, "super_seeding", False)),
                        "peers": peer_list,
                        "piece_availability": piece_availability,
                        "file_paths": file_paths,
                        "file_progress": file_progress,
                    }
                )
        return torrents


manager = None


class RpcHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        # Suppress noisy standard request logging
        pass

    def do_GET(self):
        if self.path in ("/health", "/ping", "/"):
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(b'{"status":"ok","daemon":"libtorrent_daemon"}\n')
            return
        self.send_response(404)
        self.end_headers()

    def do_POST(self):
        try:
            length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(length) if length > 0 else b"{}"
            req = json.loads(body.decode("utf-8"))
        except Exception as ex:  # noqa: BLE001
            self.send_response(400)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(json.dumps({"error": str(ex)}).encode("utf-8"))
            return

        method = req.get("method", "")
        params = req.get("params", {})
        req_id = req.get("id", 1)

        result = {}
        error = None

        try:
            if method == "session_status":
                result = manager.get_session_status()
            elif method == "add_torrent":
                result = manager.add_torrent(params)
            elif method == "remove_torrent":
                result = manager.remove_torrent(params)
            elif method == "pause_torrent":
                result = manager.pause_torrent(params)
            elif method == "resume_torrent":
                result = manager.resume_torrent(params)
            elif method == "pause_session":
                result = manager.pause_session()
            elif method == "resume_session":
                result = manager.resume_session()
            elif method == "rebind_network_interfaces":
                result = manager.rebind_network_interfaces(params)
            elif method == "force_recheck":
                result = manager.force_recheck(params)
            elif method == "force_reannounce":
                result = manager.force_reannounce(params)
            elif method == "disconnect_peer":
                result = manager.disconnect_peer(params)
            elif method == "add_trackers":
                result = manager.add_trackers(params)
            elif method == "remove_trackers":
                result = manager.remove_trackers(params)
            elif method == "set_file_priorities":
                result = manager.set_file_priorities(params)
            elif method == "set_sequential_download":
                result = manager.set_sequential_download(params)
            elif method == "set_first_last_piece_priority":
                result = manager.set_first_last_piece_priority(params)
            elif method == "set_super_seeding":
                result = manager.set_super_seeding(params)
            elif method == "set_settings":
                result = manager.set_settings(params)
            elif method == "set_torrent_limits":
                result = manager.set_torrent_limits(params)
            elif method == "set_torrent_private":
                result = manager.set_torrent_private(params)
            elif method == "move_storage":
                result = manager.move_storage(params)
            elif method == "rename_file":
                result = manager.rename_file(params)
            elif method == "rename_folder":
                result = manager.rename_folder(params)
            elif method == "get_torrents_status":
                torrents = manager.get_torrents_status()
                result = {"result": "ok", "torrents": torrents}
            else:
                error = f"Method '{method}' not found"
        except Exception as ex:  # noqa: BLE001
            error = str(ex)

        self.send_response(200 if error is None else 500)
        self.send_header("Content-Type", "application/json")
        self.end_headers()

        response = {
            "id": req_id,
            "error": error,
        }
        if isinstance(result, dict):
            response.update(result)
        else:
            response["result"] = result

        def json_fallback(obj):
            if isinstance(obj, (bytes, bytearray)):
                return obj.decode("utf-8", errors="replace")
            return str(obj)

        self.wfile.write(json.dumps(response, default=json_fallback).encode("utf-8"))


def main():
    global manager
    parser = argparse.ArgumentParser(description="Leecharr LibTorrent RPC Daemon")
    parser.add_argument(
        "--bind",
        default="127.0.0.1",
        help="Bind IP address for RPC (default: 127.0.0.1)",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=58846,
        help="Port to listen on (default: 58846)",
    )
    parser.add_argument(
        "--listen-ip",
        default="0.0.0.0",
        help="BitTorrent swarm listen IP address (default: 0.0.0.0)",
    )
    parser.add_argument(
        "--torrent-port",
        type=int,
        default=6882,
        help="BitTorrent swarm port (default: 6882)",
    )
    parser.add_argument(
        "--version-target",
        default="2.1.1",
        help="Target version profile (default: 2.1.1)",
    )
    args = parser.parse_args()

    listen_iface = f"{args.listen_ip}:{args.torrent_port}"
    manager = LibTorrentManager(
        listen_interfaces=listen_iface,
        version_target=args.version_target,
    )

    server = ThreadingHTTPServer(("127.0.0.1", args.port), RpcHandler)  # NOSONAR
    sys.stdout.write(f"libtorrent_daemon listening on 127.0.0.1:{args.port} (Swarm: {listen_iface})\n")
    sys.stdout.flush()

    def shutdown(signum, frame):
        sys.stdout.write("Shutting down libtorrent_daemon...\n")
        sys.stdout.flush()
        threading.Thread(target=server.shutdown).start()

    signal.signal(signal.SIGTERM, shutdown)
    signal.signal(signal.SIGINT, shutdown)

    try:
        server.serve_forever()  # NOSONAR
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
