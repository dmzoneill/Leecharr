import React, {
  useRef,
  useEffect,
  useState,
  useMemo,
  useCallback,
} from "react";
import { useTranslation } from "../i18n";
import { useModalRegistration } from "./ModalProvider";
import { useFocusTrap } from "../hooks/useFocusTrap";
import { useTorrentFileSubtitles } from "../api/hooks";
import type { SubtitleTrack, Torrent } from "../api/types";
import {
  isAudioFile,
  parseMediaBadges,
  buildStreamUrl,
  buildDownloadUrl,
  buildPlaylistUrl,
  buildFileStreamUrl,
  buildFileDownloadUrl,
  buildFilePlaylistUrl,
  buildExternalPlayerUrl,
  getAbsoluteUrl,
  cleanUpMediaElement,
  setSubtitleTrackActive,
  getCodecErrorMessage,
} from "../utils/mediaPlayer";
import { trackMediaPreview } from "../utils/analytics";

export interface MediaPlayerModalProps {
  isOpen: boolean;
  onClose: () => void;
  torrent?: Pick<Torrent, "id" | "name"> & Partial<Pick<Torrent, "mediaTitle">>;
  file: {
    id?: number;
    path: string;
    name?: string;
    size?: number;
  };
  subtitles?: SubtitleTrack[];
  streamUrl?: string;
  downloadUrl?: string;
  playlistUrl?: string;
}

export type SubtitleSize = "small" | "medium" | "large" | "x-large";

export function MediaPlayerModal({
  isOpen,
  onClose,
  torrent,
  file,
  subtitles: propSubtitles,
  streamUrl: propStreamUrl,
  downloadUrl: propDownloadUrl,
  playlistUrl: propPlaylistUrl,
}: MediaPlayerModalProps) {
  const { t } = useTranslation();
  const modalRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const audioRef = useRef<HTMLAudioElement>(null);

  const [activeTab, setActiveTab] = useState<"player" | "setup">("player");
  const [hasCodecError, setHasCodecError] = useState<boolean>(false);
  const [errorCode, setErrorCode] = useState<number | undefined>(undefined);
  const [copied, setCopied] = useState<boolean>(false);
  const [copiedScript, setCopiedScript] = useState<boolean>(false);
  const [copiedMpvCmd, setCopiedMpvCmd] = useState<boolean>(false);
  const [copiedVlcCmd, setCopiedVlcCmd] = useState<boolean>(false);
  const [activeSubtitleTrackId, setActiveSubtitleTrackId] = useState<
    number | "off"
  >("off");
  const [subtitleSize, setSubtitleSize] = useState<SubtitleSize>("medium");

  const fileName = useMemo(() => {
    if (file.name) return file.name;
    const parts = file.path.replace(/\\/g, "/").split("/");
    return parts[parts.length - 1] || file.path;
  }, [file.name, file.path]);

  const isAudio = useMemo(() => isAudioFile(fileName), [fileName]);
  const mediaBadges = useMemo(
    () => parseMediaBadges(fileName, torrent?.name),
    [fileName, torrent?.name],
  );

  useEffect(() => {
    if (isOpen) {
      const ext = fileName.split(".").pop()?.toLowerCase() || "unknown";
      const mimeCat = isAudio
        ? "audio"
        : ext === "mp4"
          ? "video_mp4"
          : ext === "mkv"
            ? "video_mkv"
            : "video_other";
      trackMediaPreview(mimeCat);
    }
  }, [isOpen, isAudio, fileName]);

  const streamUrl = useMemo(() => {
    if (propStreamUrl) return propStreamUrl;
    if (torrent?.id != null && file.id != null)
      return buildStreamUrl(torrent.id, file.id);
    return buildFileStreamUrl(file.path);
  }, [propStreamUrl, torrent?.id, file.id, file.path]);

  const downloadUrl = useMemo(() => {
    if (propDownloadUrl) return propDownloadUrl;
    if (torrent?.id != null && file.id != null)
      return buildDownloadUrl(torrent.id, file.id);
    return buildFileDownloadUrl(file.path);
  }, [propDownloadUrl, torrent?.id, file.id, file.path]);

  const playlistUrl = useMemo(() => {
    if (propPlaylistUrl) return propPlaylistUrl;
    if (torrent?.id != null && file.id != null)
      return buildPlaylistUrl(torrent.id, file.id);
    return buildFilePlaylistUrl(file.path);
  }, [propPlaylistUrl, torrent?.id, file.id, file.path]);

  const { data: fetchedSubtitles } = useTorrentFileSubtitles(
    isOpen && torrent?.id != null ? torrent.id : undefined,
    isOpen && file.id != null ? file.id : undefined,
  );

  const resolvedSubtitles = useMemo<SubtitleTrack[]>(() => {
    if (Array.isArray(propSubtitles)) return propSubtitles;
    if (Array.isArray(fetchedSubtitles)) return fetchedSubtitles;
    return [];
  }, [propSubtitles, fetchedSubtitles]);

  // Reset state when a new file or torrent is opened
  useEffect(() => {
    if (isOpen) {
      setActiveTab("player");
      setHasCodecError(false);
      setErrorCode(undefined);
      setCopied(false);
      setCopiedScript(false);
      setCopiedMpvCmd(false);
      setCopiedVlcCmd(false);
      // Auto-select default subtitle if available
      const defaultSub = Array.isArray(resolvedSubtitles)
        ? resolvedSubtitles.find((s) => s.isDefault)
        : undefined;
      setActiveSubtitleTrackId(defaultSub ? defaultSub.trackId : "off");
    }
  }, [isOpen, file.id, torrent?.id, resolvedSubtitles]);

  // Stream Lifecycle Cleanup on Unmount / Close:
  useEffect(() => {
    return () => {
      const videoEl = videoRef.current;
      const audioEl = audioRef.current;
      if (videoEl) {
        cleanUpMediaElement(videoEl);
      }
      if (audioEl) {
        cleanUpMediaElement(audioEl);
      }
    };
  }, []);

  const handleClose = useCallback(() => {
    if (videoRef.current) {
      cleanUpMediaElement(videoRef.current);
    }
    if (audioRef.current) {
      cleanUpMediaElement(audioRef.current);
    }
    onClose();
  }, [onClose]);

  useModalRegistration({
    id: "media-player-modal",
    isOpen,
    onClose: handleClose,
    modalRef,
  });

  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onClose: handleClose,
  });

  const setContainerRef = (el: HTMLDivElement | null) => {
    (modalRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
    (trapRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
  };

  // Handle HTMLMediaElement onError event
  const handleMediaError = useCallback(
    (e: React.SyntheticEvent<HTMLMediaElement, Event>) => {
      const target = e.currentTarget;
      const code = target.error?.code ?? 4;
      setErrorCode(code);
      setHasCodecError(true);
    },
    [],
  );

  // Subtitle track switching synchronization with HTML5 textTracks
  const handleSubtitleChange = useCallback(
    (val: string) => {
      if (val === "off") {
        setActiveSubtitleTrackId("off");
        if (videoRef.current) {
          setSubtitleTrackActive(
            videoRef.current.textTracks,
            "off",
            resolvedSubtitles,
          );
        }
      } else {
        const id = parseInt(val, 10);
        setActiveSubtitleTrackId(id);
        if (videoRef.current) {
          setSubtitleTrackActive(
            videoRef.current.textTracks,
            id,
            resolvedSubtitles,
          );
        }
      }
    },
    [resolvedSubtitles],
  );

  const handleCopyStreamUrl = useCallback(() => {
    const fullUrl = getAbsoluteUrl(streamUrl);
    if (typeof navigator !== "undefined" && navigator.clipboard?.writeText) {
      navigator.clipboard.writeText(fullUrl).then(() => {
        setCopied(true);
        setTimeout(() => setCopied(false), 2500);
      });
    }
  }, [streamUrl]);

  const handleRetryPlayback = useCallback(() => {
    setHasCodecError(false);
    setErrorCode(undefined);
    const media = videoRef.current ?? audioRef.current;
    if (media) {
      media.load();
      media.play().catch(() => {
        // Ignored
      });
    }
  }, []);

  // Keyboard Shortcuts: Space, Arrows, M, F, C, [/]
  useEffect(() => {
    if (!isOpen) return;

    const SPEED_STEPS = [0.75, 1, 1.25, 1.5, 2];

    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const tagName = target?.tagName?.toLowerCase();
      if (
        tagName === "input" ||
        tagName === "textarea" ||
        tagName === "select" ||
        target?.isContentEditable
      ) {
        return;
      }

      const media = videoRef.current ?? audioRef.current;
      if (!media) return;

      // Space: play/pause
      if (e.key === " " || e.code === "Space") {
        e.preventDefault();
        if (media.paused) {
          media.play().catch(() => {});
        } else {
          media.pause();
        }
        return;
      }

      // ArrowLeft / ArrowRight: seek -/+ 5s, with Shift 30s
      if (e.key === "ArrowLeft") {
        e.preventDefault();
        const delta = e.shiftKey ? 30 : 5;
        media.currentTime = Math.max(0, media.currentTime - delta);
        return;
      }
      if (e.key === "ArrowRight") {
        e.preventDefault();
        const delta = e.shiftKey ? 30 : 5;
        const dur = Number.isFinite(media.duration) ? media.duration : Infinity;
        media.currentTime = Math.min(dur, media.currentTime + delta);
        return;
      }

      // ArrowUp / ArrowDown: volume -/+ 5%
      if (e.key === "ArrowUp") {
        e.preventDefault();
        media.volume = Math.min(
          1,
          Math.round((media.volume + 0.05) * 100) / 100,
        );
        if (media.muted && media.volume > 0) media.muted = false;
        return;
      }
      if (e.key === "ArrowDown") {
        e.preventDefault();
        media.volume = Math.max(
          0,
          Math.round((media.volume - 0.05) * 100) / 100,
        );
        return;
      }

      // M / m: mute
      if (e.key === "m" || e.key === "M") {
        e.preventDefault();
        media.muted = !media.muted;
        return;
      }

      // F / f: fullscreen
      if (e.key === "f" || e.key === "F") {
        e.preventDefault();
        if (!document.fullscreenElement) {
          const targetEl = videoRef.current ?? modalRef.current;
          if (targetEl?.requestFullscreen) {
            targetEl.requestFullscreen().catch(() => {});
          }
        } else {
          if (document.exitFullscreen) {
            document.exitFullscreen().catch(() => {});
          }
        }
        return;
      }

      // C / c: cycle subtitles
      if (e.key === "c" || e.key === "C") {
        e.preventDefault();
        if (Array.isArray(resolvedSubtitles) && resolvedSubtitles.length > 0) {
          const options: (number | "off")[] = [
            "off",
            ...resolvedSubtitles.map((s) => s.trackId),
          ];
          const currentIdx = options.indexOf(activeSubtitleTrackId);
          const nextIdx = (currentIdx + 1) % options.length;
          const nextVal = options[nextIdx];
          handleSubtitleChange(String(nextVal));
        }
        return;
      }

      // [ / ]: speed 0.75x, 1x, 1.25x, 1.5x, 2x
      if (e.key === "[") {
        e.preventDefault();
        const currentRate = media.playbackRate;
        let nextRate = SPEED_STEPS[0];
        for (let i = SPEED_STEPS.length - 1; i >= 0; i--) {
          if (SPEED_STEPS[i] < currentRate - 0.05) {
            nextRate = SPEED_STEPS[i];
            break;
          }
        }
        media.playbackRate = nextRate;
        return;
      }
      if (e.key === "]") {
        e.preventDefault();
        const currentRate = media.playbackRate;
        let nextRate = SPEED_STEPS[SPEED_STEPS.length - 1];
        for (let i = 0; i < SPEED_STEPS.length; i++) {
          if (SPEED_STEPS[i] > currentRate + 0.05) {
            nextRate = SPEED_STEPS[i];
            break;
          }
        }
        media.playbackRate = nextRate;
        return;
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, resolvedSubtitles, activeSubtitleTrackId, handleSubtitleChange]);

  if (!isOpen) return null;

  const titleText = torrent?.mediaTitle || torrent?.name || fileName;
  const vlcUrl = buildExternalPlayerUrl("vlc", streamUrl);
  const mpvUrl = buildExternalPlayerUrl("mpv", streamUrl);

  const fontSizes: Record<SubtitleSize, string> = {
    small: "14px",
    medium: "18px",
    large: "22px",
    "x-large": "28px",
  };

  return (
    <div
      className="modal-overlay"
      role="dialog"
      aria-modal="true"
      aria-labelledby="media-player-title"
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.85)",
        backdropFilter: "blur(8px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 9999,
        padding: "1rem",
      }}
      onClick={handleClose}
    >
      <style>{`
        .media-player-dialog video::cue {
          font-size: ${fontSizes[subtitleSize]};
          background-color: rgba(0, 0, 0, 0.75);
          color: #ffffff;
          line-height: 1.3;
        }
        .media-badge {
          display: inline-flex;
          align-items: center;
          font-size: 0.68rem;
          font-weight: 700;
          padding: 0.15rem 0.45rem;
          border-radius: 4px;
          background: rgba(255, 255, 255, 0.12);
          color: var(--text-primary, #ffffff);
          border: 1px solid rgba(255, 255, 255, 0.2);
          letter-spacing: 0.5px;
          text-transform: uppercase;
        }
      `}</style>

      <div
        ref={setContainerRef}
        className="card media-player-dialog"
        style={{
          width: "900px",
          maxWidth: "95vw",
          maxHeight: "92vh",
          display: "flex",
          flexDirection: "column",
          borderRadius: "12px",
          overflow: "hidden",
          border: "1px solid var(--border-light, #333)",
          boxShadow: "0 24px 64px rgba(0, 0, 0, 0.8)",
          padding: 0,
          backgroundColor: "var(--bg-primary, #18191c)",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Modal Header */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            padding: "0.85rem 1.25rem",
            borderBottom: "1px solid var(--border-light, #333)",
            backgroundColor: "var(--bg-secondary, #222328)",
            gap: "0.75rem",
          }}
        >
          <div style={{ minWidth: 0, flex: 1 }}>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
                flexWrap: "wrap",
              }}
            >
              <h3
                id="media-player-title"
                style={{
                  margin: 0,
                  fontSize: "1.1rem",
                  fontWeight: 600,
                  whiteSpace: "nowrap",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                  maxWidth: "500px",
                }}
              >
                {titleText}
              </h3>
              {Array.isArray(mediaBadges?.badges) &&
                mediaBadges.badges.map((b) => (
                  <span key={b} className="media-badge">
                    {b}
                  </span>
                ))}
            </div>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted, #888)",
                whiteSpace: "nowrap",
                overflow: "hidden",
                textOverflow: "ellipsis",
                marginTop: "0.2rem",
              }}
              title={fileName}
            >
              📄 {fileName}
            </div>
          </div>

          {/* Header Tab Switcher */}
          <div
            style={{ display: "flex", gap: "0.35rem", alignItems: "center" }}
          >
            <button
              type="button"
              className={`btn btn-xs ${activeTab === "player" ? "btn-primary" : "btn-default"}`}
              onClick={() => setActiveTab("player")}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.3rem",
                fontWeight: 600,
                padding: "0.25rem 0.55rem",
              }}
            >
              <span>▶</span> {t("mediaPlayer.tabPlayer", "Player")}
            </button>
            <button
              type="button"
              className={`btn btn-xs ${activeTab === "setup" ? "btn-primary" : "btn-default"}`}
              onClick={() => setActiveTab("setup")}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.3rem",
                fontWeight: 600,
                padding: "0.25rem 0.55rem",
              }}
            >
              <span>⚙</span>{" "}
              {t("mediaPlayer.tabSetup", "External Players & Setup")}
            </button>
          </div>

          <button
            type="button"
            className="btn btn-sm btn-default"
            aria-label={t("mediaPlayer.close", undefined, "Close media player")}
            title={t("mediaPlayer.close", undefined, "Close media player")}
            onClick={handleClose}
            style={{
              padding: "0.35rem 0.65rem",
              fontSize: "1.1rem",
              lineHeight: 1,
              borderRadius: "6px",
              cursor: "pointer",
            }}
          >
            ✕
          </button>
        </div>

        {/* Media / Fallback Area or Setup Instructions */}
        {activeTab === "setup" ? (
          <div
            style={{
              flex: 1,
              display: "flex",
              flexDirection: "column",
              backgroundColor: "var(--bg-primary, #18191c)",
              minHeight: "360px",
              maxHeight: "65vh",
              overflowY: "auto",
              padding: "1.5rem",
              gap: "1.25rem",
              color: "#e2e8f0",
              lineHeight: 1.5,
            }}
          >
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                flexWrap: "wrap",
                gap: "0.5rem",
              }}
            >
              <h4
                style={{
                  margin: 0,
                  fontSize: "1.15rem",
                  color: "#60a5fa",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                }}
              >
                <span>📺</span> External Player Streaming & Linux Protocol Setup
              </h4>
              <button
                type="button"
                className="btn btn-xs btn-default"
                onClick={() => setActiveTab("player")}
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "0.3rem",
                }}
              >
                <span>◀</span> Return to Player
              </button>
            </div>

            {/* Section 1: Instant M3U Playlist */}
            <div
              style={{
                backgroundColor: "rgba(255, 255, 255, 0.04)",
                border: "1px solid rgba(255, 255, 255, 0.08)",
                borderRadius: "8px",
                padding: "1rem",
              }}
            >
              <h5
                style={{
                  margin: "0 0 0.4rem 0",
                  color: "#34d399",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.4rem",
                  fontSize: "0.95rem",
                }}
              >
                <span>⚡</span> 1. Direct Playlist (.m3u) — Instant, Zero Setup
              </h5>
              <p
                style={{
                  margin: "0 0 0.75rem 0",
                  fontSize: "0.85rem",
                  color: "#94a3b8",
                }}
              >
                Most Linux desktop players (VLC, MPV, Celluloid, Totem) open
                standard <code>.m3u</code> playlist files automatically when
                downloaded or clicked.
              </p>
              <div
                style={{
                  display: "flex",
                  gap: "0.6rem",
                  flexWrap: "wrap",
                  alignItems: "center",
                }}
              >
                <a
                  href={playlistUrl}
                  download={`${fileName}.m3u`}
                  className="btn btn-sm btn-success"
                  style={{
                    textDecoration: "none",
                    fontWeight: 600,
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.4rem",
                  }}
                >
                  <span>📥</span> Download M3U Playlist
                </a>
                <button
                  type="button"
                  className="btn btn-sm btn-default"
                  onClick={handleCopyStreamUrl}
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.4rem",
                  }}
                >
                  <span>📋</span>{" "}
                  {copied ? "Stream URL Copied!" : "Copy Stream URL"}
                </button>
              </div>
            </div>

            {/* Section 2: Command Line Playback */}
            <div
              style={{
                backgroundColor: "rgba(255, 255, 255, 0.04)",
                border: "1px solid rgba(255, 255, 255, 0.08)",
                borderRadius: "8px",
                padding: "1rem",
              }}
            >
              <h5
                style={{
                  margin: "0 0 0.4rem 0",
                  color: "#38bdf8",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.4rem",
                  fontSize: "0.95rem",
                }}
              >
                <span>💻</span> 2. Command Line Playback (MPV & VLC)
              </h5>
              <p
                style={{
                  margin: "0 0 0.5rem 0",
                  fontSize: "0.85rem",
                  color: "#94a3b8",
                }}
              >
                Stream directly from your Linux terminal using MPV or VLC:
              </p>
              <div
                style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.5rem",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.5rem",
                  }}
                >
                  <code
                    style={{
                      flex: 1,
                      backgroundColor: "#0f172a",
                      padding: "0.4rem 0.6rem",
                      borderRadius: "4px",
                      fontSize: "0.8rem",
                      overflowX: "auto",
                    }}
                  >
                    mpv &quot;{getAbsoluteUrl(streamUrl)}&quot;
                  </code>
                  <button
                    type="button"
                    className="btn btn-xs btn-default"
                    onClick={() => {
                      if (navigator.clipboard?.writeText) {
                        navigator.clipboard.writeText(
                          `mpv "${getAbsoluteUrl(streamUrl)}"`,
                        );
                        setCopiedMpvCmd(true);
                        setTimeout(() => setCopiedMpvCmd(false), 2000);
                      }
                    }}
                  >
                    {copiedMpvCmd ? "Copied!" : "Copy"}
                  </button>
                </div>
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.5rem",
                  }}
                >
                  <code
                    style={{
                      flex: 1,
                      backgroundColor: "#0f172a",
                      padding: "0.4rem 0.6rem",
                      borderRadius: "4px",
                      fontSize: "0.8rem",
                      overflowX: "auto",
                    }}
                  >
                    vlc &quot;{getAbsoluteUrl(streamUrl)}&quot;
                  </code>
                  <button
                    type="button"
                    className="btn btn-xs btn-default"
                    onClick={() => {
                      if (navigator.clipboard?.writeText) {
                        navigator.clipboard.writeText(
                          `vlc "${getAbsoluteUrl(streamUrl)}"`,
                        );
                        setCopiedVlcCmd(true);
                        setTimeout(() => setCopiedVlcCmd(false), 2000);
                      }
                    }}
                  >
                    {copiedVlcCmd ? "Copied!" : "Copy"}
                  </button>
                </div>
              </div>
            </div>

            {/* Section 3: Linux Desktop URI Handlers */}
            <div
              style={{
                backgroundColor: "rgba(255, 255, 255, 0.04)",
                border: "1px solid rgba(255, 255, 255, 0.08)",
                borderRadius: "8px",
                padding: "1rem",
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginBottom: "0.5rem",
                  flexWrap: "wrap",
                  gap: "0.4rem",
                }}
              >
                <h5
                  style={{
                    margin: 0,
                    color: "#f59e0b",
                    display: "flex",
                    alignItems: "center",
                    gap: "0.4rem",
                    fontSize: "0.95rem",
                  }}
                >
                  <span>🐧</span> 3. One-Click Browser Integration (vlc:// &
                  web+mpv://)
                </h5>
                <button
                  type="button"
                  className="btn btn-xs btn-primary"
                  onClick={() => {
                    const script = `mkdir -p ~/.local/bin ~/.local/share/applications

cat << 'EOF' > ~/.local/bin/vlc-stream-handler
#!/usr/bin/env bash
url="\${1#vlc://}"; url="\${url#vlc:}"
[[ -n "$url" ]] && exec /usr/bin/vlc "$url"
EOF
chmod +x ~/.local/bin/vlc-stream-handler

cat << 'EOF' > ~/.local/bin/mpv-stream-handler
#!/usr/bin/env bash
url="\${1#web+mpv://}"; url="\${url#mpv://}"; url="\${url#web+mpv:}"; url="\${url#mpv:}"
[[ -n "$url" ]] && exec /usr/bin/mpv "$url"
EOF
chmod +x ~/.local/bin/mpv-stream-handler

cat << 'EOF' > ~/.local/share/applications/vlc-stream-handler.desktop
[Desktop Entry]
Name=VLC Stream Handler
Exec=/home/$USER/.local/bin/vlc-stream-handler %u
Type=Application
Terminal=false
NoDisplay=true
MimeType=x-scheme-handler/vlc;
EOF

cat << 'EOF' > ~/.local/share/applications/mpv-stream-handler.desktop
[Desktop Entry]
Name=MPV Stream Handler
Exec=/home/$USER/.local/bin/mpv-stream-handler %u
Type=Application
Terminal=false
NoDisplay=true
MimeType=x-scheme-handler/web+mpv;x-scheme-handler/mpv;
EOF

xdg-mime default vlc-stream-handler.desktop x-scheme-handler/vlc
xdg-mime default mpv-stream-handler.desktop x-scheme-handler/web+mpv
xdg-mime default mpv-stream-handler.desktop x-scheme-handler/mpv
update-desktop-database ~/.local/share/applications/`;
                    if (navigator.clipboard?.writeText) {
                      navigator.clipboard.writeText(script);
                      setCopiedScript(true);
                      setTimeout(() => setCopiedScript(false), 2500);
                    }
                  }}
                >
                  {copiedScript ? "Copied Script!" : "Copy Linux Setup Script"}
                </button>
              </div>
              <p
                style={{
                  margin: "0 0 0.5rem 0",
                  fontSize: "0.85rem",
                  color: "#94a3b8",
                }}
              >
                Standard Linux distributions do not register <code>vlc://</code>{" "}
                or <code>web+mpv://</code> URI schemes by default. Click the
                button above to copy the setup script, run it in your terminal,
                and browser links will launch VLC and MPV directly with a single
                click.
              </p>
            </div>
          </div>
        ) : (
          <div
            style={{
              position: "relative",
              flex: 1,
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
              justifyContent: "center",
              backgroundColor: "#000000",
              minHeight: "360px",
              maxHeight: "65vh",
              overflow: "hidden",
            }}
          >
            {/* Codec Error Fallback Card */}
            {hasCodecError ? (
              <div
                data-testid="codec-error-card"
                style={{
                  display: "flex",
                  flexDirection: "column",
                  alignItems: "center",
                  justifyContent: "center",
                  padding: "2rem",
                  textAlign: "center",
                  maxWidth: "600px",
                  color: "#f8f9fa",
                  gap: "1rem",
                }}
              >
                <div style={{ fontSize: "2.5rem" }}>⚠️</div>
                <h4
                  style={{ margin: 0, fontSize: "1.25rem", color: "#ffb703" }}
                >
                  {t(
                    "mediaPlayer.playbackError",
                    "Browser Codec Playback Error",
                  )}
                </h4>
                <p
                  style={{
                    margin: 0,
                    fontSize: "0.9rem",
                    color: "#d1d5db",
                    lineHeight: 1.5,
                  }}
                >
                  {getCodecErrorMessage(errorCode, fileName)}
                </p>

                {/* Action Buttons */}
                <div
                  style={{
                    display: "flex",
                    flexWrap: "wrap",
                    gap: "0.6rem",
                    justifyContent: "center",
                    marginTop: "0.5rem",
                  }}
                >
                  <a
                    href={vlcUrl}
                    className="btn btn-primary"
                    role="button"
                    style={{
                      textDecoration: "none",
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.4rem",
                      fontWeight: 600,
                    }}
                  >
                    <span>📺</span> {t("mediaPlayer.openInVlc", "Open in VLC")}
                  </a>
                  <a
                    href={mpvUrl}
                    className="btn btn-secondary"
                    role="button"
                    style={{
                      textDecoration: "none",
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.4rem",
                      fontWeight: 600,
                    }}
                  >
                    <span>⚡</span> {t("mediaPlayer.openInMpv", "Open in MPV")}
                  </a>
                  <a
                    href={playlistUrl}
                    download={`${fileName}.m3u`}
                    className="btn btn-success"
                    role="button"
                    style={{
                      textDecoration: "none",
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.4rem",
                      fontWeight: 600,
                    }}
                  >
                    <span>📥</span>{" "}
                    {t("mediaPlayer.downloadM3u", "M3U Playlist")}
                  </a>
                  <a
                    href={downloadUrl}
                    download={fileName}
                    className="btn btn-default"
                    role="button"
                    style={{
                      textDecoration: "none",
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.4rem",
                    }}
                  >
                    <span>⬇️</span>{" "}
                    {t("mediaPlayer.directDownload", "Direct Download")}
                  </a>
                  <button
                    type="button"
                    className="btn btn-default"
                    onClick={handleCopyStreamUrl}
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.4rem",
                    }}
                  >
                    <span>📋</span>{" "}
                    {copied
                      ? t("mediaPlayer.copied", "Copied!")
                      : t("mediaPlayer.copyStreamUrl", "Copy Stream URL")}
                  </button>
                  <button
                    type="button"
                    className="btn btn-default"
                    onClick={() => setActiveTab("setup")}
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.4rem",
                    }}
                  >
                    <span>📖</span>{" "}
                    {t("mediaPlayer.setupGuide", "Linux Setup Guide")}
                  </button>
                  <button
                    type="button"
                    className="btn btn-default"
                    onClick={handleRetryPlayback}
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.4rem",
                    }}
                  >
                    <span>🔄</span> {t("mediaPlayer.retry", "Retry")}
                  </button>
                </div>
              </div>
            ) : isAudio ? (
              <div
                style={{
                  display: "flex",
                  flexDirection: "column",
                  alignItems: "center",
                  justifyContent: "center",
                  gap: "1.5rem",
                  padding: "2rem",
                  width: "100%",
                }}
              >
                <div
                  style={{
                    width: "120px",
                    height: "120px",
                    borderRadius: "50%",
                    backgroundColor: "rgba(52, 152, 219, 0.15)",
                    border: "2px solid rgba(52, 152, 219, 0.4)",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontSize: "3rem",
                  }}
                >
                  🎵
                </div>
                <audio
                  ref={audioRef}
                  controls
                  autoPlay
                  preload="metadata"
                  src={streamUrl}
                  onError={handleMediaError}
                  style={{ width: "80%", maxWidth: "500px" }}
                />
              </div>
            ) : (
              <video
                ref={videoRef}
                controls
                autoPlay
                preload="metadata"
                src={streamUrl}
                onError={handleMediaError}
                style={{
                  width: "100%",
                  height: "100%",
                  maxHeight: "65vh",
                  backgroundColor: "#000",
                  outline: "none",
                }}
              >
                {Array.isArray(resolvedSubtitles) &&
                  resolvedSubtitles.map((track) => (
                    <track
                      key={track.trackId}
                      kind="subtitles"
                      label={
                        track.title ||
                        `${track.language || "Subtitle"} (${track.twoLetterCode || track.format || `Track ${track.trackId}`})`
                      }
                      src={track.url}
                      srcLang={track.twoLetterCode || "en"}
                      default={track.isDefault}
                    />
                  ))}
              </video>
            )}
          </div>
        )}

        {/* Footer Toolbar: Subtitles, Size, & External Links */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            padding: "0.75rem 1.25rem",
            borderTop: "1px solid var(--border-light, #333)",
            backgroundColor: "var(--bg-secondary, #222328)",
            flexWrap: "wrap",
            gap: "0.75rem",
          }}
        >
          {/* Subtitle controls (for video) */}
          {!isAudio && (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
                flexWrap: "wrap",
              }}
            >
              <label
                htmlFor="subtitle-select"
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-muted, #aaa)",
                  fontWeight: 500,
                }}
              >
                💬 {t("mediaPlayer.subtitles", "Subtitles:")}
              </label>
              <select
                id="subtitle-select"
                aria-label={t(
                  "mediaPlayer.selectSubtitleTrack",
                  "Select subtitle track",
                )}
                className="form-control"
                style={{
                  fontSize: "0.8rem",
                  padding: "0.2rem 0.5rem",
                  height: "auto",
                  width: "auto",
                  minWidth: "140px",
                }}
                value={activeSubtitleTrackId}
                onChange={(e) => handleSubtitleChange(e.target.value)}
              >
                <option value="off">{t("mediaPlayer.off", "Off")}</option>
                {Array.isArray(resolvedSubtitles) &&
                  resolvedSubtitles.map((sub) => {
                    const labelParts = [
                      sub.language || sub.title || `Track ${sub.trackId}`,
                    ];
                    if (sub.isForced) labelParts.push("(Forced)");
                    if (sub.isHearingImpaired) labelParts.push("(CC)");
                    if (sub.isExternal) labelParts.push("[Ext]");
                    return (
                      <option key={sub.trackId} value={sub.trackId}>
                        {labelParts.join(" ")}
                      </option>
                    );
                  })}
              </select>

              {activeSubtitleTrackId !== "off" && (
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.3rem",
                  }}
                >
                  <label
                    htmlFor="subtitle-size-select"
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #aaa)",
                    }}
                  >
                    {t("mediaPlayer.size", "Size:")}
                  </label>
                  <select
                    id="subtitle-size-select"
                    aria-label={t(
                      "mediaPlayer.subtitleTextSize",
                      "Subtitle text size",
                    )}
                    className="form-control"
                    style={{
                      fontSize: "0.75rem",
                      padding: "0.15rem 0.4rem",
                      height: "auto",
                      width: "auto",
                    }}
                    value={subtitleSize}
                    onChange={(e) =>
                      setSubtitleSize(e.target.value as SubtitleSize)
                    }
                  >
                    <option value="small">
                      {t("mediaPlayer.sizeSmall", "Small")}
                    </option>
                    <option value="medium">
                      {t("mediaPlayer.sizeNormal", "Normal")}
                    </option>
                    <option value="large">
                      {t("mediaPlayer.sizeLarge", "Large")}
                    </option>
                    <option value="x-large">
                      {t("mediaPlayer.sizeExtraLarge", "Extra Large")}
                    </option>
                  </select>
                </div>
              )}
            </div>
          )}

          {/* Quick External Player Toolbar Links */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              marginLeft: "auto",
            }}
          >
            <a
              href={vlcUrl}
              className="btn btn-xs btn-default"
              title={t(
                "mediaPlayer.tooltipVlc",
                "Open stream in VLC media player",
              )}
              style={{ textDecoration: "none" }}
            >
              VLC
            </a>
            <a
              href={mpvUrl}
              className="btn btn-xs btn-default"
              title={t("mediaPlayer.tooltipMpv", "Open stream in MPV player")}
              style={{ textDecoration: "none" }}
            >
              MPV
            </a>
            <a
              href={playlistUrl}
              download={`${fileName}.m3u`}
              className="btn btn-xs btn-default"
              title={t(
                "mediaPlayer.tooltipPlaylist",
                "Download M3U playlist for desktop media players",
              )}
              style={{ textDecoration: "none" }}
            >
              {t("mediaPlayer.playlist", "Playlist (.m3u)")}
            </a>
            <button
              type="button"
              className="btn btn-xs btn-default"
              onClick={handleCopyStreamUrl}
              title={t("mediaPlayer.tooltipCopyUrl", "Copy direct stream URL")}
            >
              {copied
                ? t("mediaPlayer.copied", "Copied!")
                : t("mediaPlayer.copyUrl", "Copy URL")}
            </button>
            <a
              href={downloadUrl}
              download={fileName}
              className="btn btn-xs btn-default"
              title={t("mediaPlayer.tooltipDownload", "Download media file")}
              style={{ textDecoration: "none" }}
            >
              {t("mediaPlayer.download", "Download")}
            </a>
          </div>
        </div>
      </div>
    </div>
  );
}

export default MediaPlayerModal;
