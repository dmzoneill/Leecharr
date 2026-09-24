#!/usr/bin/env python3
"""
Leecharr LibTorrent Daemon (Rasterbar C++ / Python Sidecar)
Provides an embedded HTTP JSON-RPC bridge for LibTorrentDownloadEngine on port 58846.
"""

import argparse
import base64
import json
import signal
import sys
import threading
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler

try:
    import libtorrent as lt
except ImportError:
    sys.stderr.write("ERROR: python3-libtorrent is not installed.\n")
    sys.exit(1)


class LibTorrentManager:
    def __init__(self, listen_interfaces="0.0.0.0:6882", version_target="2.1.1"):
        self.lock = threading.Lock()
        self.version_target = version_target
        settings = {
            "listen_interfaces": listen_interfaces,
            "alert_mask": lt.alert.category_t.error_notification
            | lt.alert.category_t.status_notification,
            "enable_dht": True,
            "enable_lsd": True,
            "enable_upnp": True,
            "enable_natpmp": True,
        }
        self.session = lt.session(settings)

    def find_handle(self, info_hash):
        if not info_hash:
            return None
        target = info_hash.lower()
        with self.lock:
            for h in self.session.get_torrents():
                if not h.is_valid():
                    continue
                st = h.status()
                ih = ""
                if hasattr(st, "info_hashes") and st.info_hashes.has_v1():
                    ih = str(st.info_hashes.v1)
                elif hasattr(st, "info_hash"):
                    ih = str(st.info_hash)
                else:
                    ih = str(h.info_hash())
                if ih.lower() == target:
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
        if "ti" in params and params["ti"]:
            raw = base64.b64decode(params["ti"])
            ti = lt.torrent_info(raw)
            atp = lt.add_torrent_params()
            atp.ti = ti
        elif "url" in params and params["url"]:
            atp = lt.parse_magnet_uri(params["url"])
        elif "info_hash" in params and params["info_hash"]:
            atp = lt.add_torrent_params()
            atp.info_hash = lt.sha1_hash(bytes.fromhex(params["info_hash"]))
        else:
            raise ValueError("Missing torrent info or magnet url")

        if "save_path" in params and params["save_path"]:
            atp.save_path = params["save_path"]
        if "name" in params and params["name"]:
            atp.name = params["name"]

        ih_str = params.get("info_hash")
        existing = self.find_handle(ih_str) if ih_str else None
        if existing:
            return {"status": "already_added", "info_hash": ih_str}

        with self.lock:
            h = self.session.add_torrent(atp)
            trackers = params.get("trackers")
            if trackers and isinstance(trackers, list):
                for tr in trackers:
                    try:
                        h.add_tracker({"url": str(tr), "tier": 0})
                    except Exception:
                        pass
            try:
                if hasattr(h, "auto_managed"):
                    h.auto_managed(True)
                if hasattr(h, "resume"):
                    h.resume()
            except Exception:
                pass

            st = h.status()
            ih = (
                str(st.info_hashes.v1)
                if hasattr(st, "info_hashes") and st.info_hashes.has_v1()
                else str(st.info_hash)
            )
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
                except Exception:
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
        if h:
            with self.lock:
                h.force_reannounce()
            return {"status": "reannounced"}
        return {"status": "not_found"}

    def add_trackers(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            trackers = params.get("trackers", [])
            with self.lock:
                for t in trackers:
                    try:
                        h.add_tracker({"url": t, "tier": 0})
                    except Exception:
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

    def set_settings(self, params):
        settings = {}
        if "download_rate_limit" in params:
            settings["download_rate_limit"] = int(params["download_rate_limit"])
        if "upload_rate_limit" in params:
            settings["upload_rate_limit"] = int(params["upload_rate_limit"])
        if settings:
            with self.lock:
                self.session.apply_settings(settings)
        return {"status": "settings_applied"}

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

    def move_storage(self, params):
        h = self.find_handle(params.get("info_hash"))
        if h:
            save_path = params.get("save_path")
            flags = int(params.get("flags", 1))
            with self.lock:
                h.move_storage(save_path, flags)
            return {"status": "storage_moved"}
        return {"status": "not_found"}

    def get_torrents_status(self):
        torrents = []
        with self.lock:
            handles = list(self.session.get_torrents())
            for h in handles:
                if not h.is_valid():
                    continue
                st = h.status()
                ih = ""
                if hasattr(st, "info_hashes") and st.info_hashes.has_v1():
                    ih = str(st.info_hashes.v1)
                elif hasattr(st, "info_hash"):
                    ih = str(st.info_hash)
                else:
                    ih = str(h.info_hash())

                peer_list = []
                try:
                    for pi in h.get_peer_info():
                        client_raw = getattr(pi, "client", "")
                        if isinstance(client_raw, (bytes, bytearray)):
                            client_str = client_raw.decode("utf-8", errors="replace")
                        else:
                            client_str = (
                                str(client_raw) if client_raw is not None else ""
                            )

                        peer_list.append(
                            {
                                "ip": pi.ip[0]
                                if isinstance(pi.ip, tuple)
                                else str(pi.ip),
                                "port": pi.ip[1] if isinstance(pi.ip, tuple) else 0,
                                "client": client_str,
                                "flags": str(getattr(pi, "flags", "")),
                                "progress": float(getattr(pi, "progress", 0.0)),
                                "download_rate": int(getattr(pi, "down_speed", 0)),
                                "upload_rate": int(getattr(pi, "up_speed", 0)),
                                "total_download": int(getattr(pi, "total_download", 0)),
                                "total_upload": int(getattr(pi, "total_upload", 0)),
                                "is_encrypted": bool(
                                    getattr(pi, "rc4_encrypted", False)
                                    or getattr(pi, "plaintext_encrypted", False)
                                ),
                                "is_utp": bool(getattr(pi, "connection_type", 0) == 1),
                                "is_incoming": bool(
                                    not getattr(pi, "local_connection", True)
                                ),
                                "is_choked": bool(getattr(pi, "choked", False)),
                                "is_interested": bool(
                                    getattr(pi, "interesting", False)
                                ),
                            }
                        )
                except Exception:
                    pass

                state_str = str(st.state).lower()
                if "." in state_str:
                    state_str = state_str.split(".")[-1]

                torrents.append(
                    {
                        "info_hash": ih,
                        "progress": float(st.progress),
                        "download_rate": int(st.download_rate),
                        "upload_rate": int(st.upload_rate),
                        "total_done": int(st.total_done),
                        "total_uploaded": int(st.total_upload),
                        "state": state_str,
                        "num_seeds": int(st.num_seeds),
                        "num_peers": int(st.num_peers),
                        "peers": peer_list,
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
        except Exception as ex:
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
            elif method == "add_trackers":
                result = manager.add_trackers(params)
            elif method == "remove_trackers":
                result = manager.remove_trackers(params)
            elif method == "set_file_priorities":
                result = manager.set_file_priorities(params)
            elif method == "set_settings":
                result = manager.set_settings(params)
            elif method == "set_torrent_limits":
                result = manager.set_torrent_limits(params)
            elif method == "move_storage":
                result = manager.move_storage(params)
            elif method == "get_torrents_status":
                torrents = manager.get_torrents_status()
                result = {"result": "ok", "torrents": torrents}
            else:
                error = f"Method '{method}' not found"
        except Exception as ex:
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
        "--port", type=int, default=58846, help="Port to listen on (default: 58846)"
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

    server = ThreadingHTTPServer((args.bind, args.port), RpcHandler)
    sys.stdout.write(
        f"libtorrent_daemon listening on {args.bind}:{args.port} (Swarm: {listen_iface})\n"
    )
    sys.stdout.flush()

    def shutdown(signum, frame):
        sys.stdout.write("Shutting down libtorrent_daemon...\n")
        sys.stdout.flush()
        threading.Thread(target=server.shutdown).start()

    signal.signal(signal.SIGTERM, shutdown)
    signal.signal(signal.SIGINT, shutdown)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
