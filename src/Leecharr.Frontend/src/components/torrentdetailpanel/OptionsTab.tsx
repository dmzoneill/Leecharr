import { useState, useEffect, useRef } from "react";
import { useTranslation } from "../../i18n";
import { useUpdateTorrent } from "../../api/hooks";
import type { Torrent } from "../../api/types";

export function OptionsTab({ torrent }: { torrent: Torrent }) {
  const { t } = useTranslation();
  const updateTorrent = useUpdateTorrent();
  const lastTorrentIdRef = useRef(torrent.id);
  const [priority, setPriority] = useState(String(torrent.priority ?? 1));
  const [uploadLimit, setUploadLimit] = useState(torrent.uploadLimit ?? 0);
  const [downloadLimit, setDownloadLimit] = useState(
    torrent.downloadLimit ?? 0,
  );
  const [initialSeeding, setInitialSeeding] = useState(
    Boolean(torrent.initialSeeding),
  );
  const [forceStart, setForceStart] = useState(Boolean(torrent.forceStart));
  const [sequentialDownload, setSequentialDownload] = useState(
    Boolean(torrent.sequentialDownload),
  );
  const [isPrivate, setIsPrivate] = useState(Boolean(torrent.isPrivate));
  const [active, setActive] = useState(
    torrent.active ??
      (torrent.status !== "paused" && torrent.status !== "stopped"),
  );
  const [label, setLabel] = useState(torrent.label ?? "");
  const [announceInterval, setAnnounceInterval] = useState(
    torrent.announceInterval || 1800,
  );
  const [nextUpdate, setNextUpdate] = useState(torrent.nextUpdate || 1800);
  const [threshold, setThreshold] = useState(torrent.threshold || 1);
  const [smallTorrentLimit, setSmallTorrentLimit] = useState(
    torrent.smallTorrentLimit || 50,
  );
  const [targetRatio, setTargetRatio] = useState(torrent.targetRatio ?? 0);
  const [targetSeedTimeMinutes, setTargetSeedTimeMinutes] = useState(
    torrent.targetSeedTimeMinutes ?? 0,
  );
  const [shareLimitAction, setShareLimitAction] = useState(
    torrent.shareLimitAction || "Default",
  );
  const [dirty, setDirty] = useState(false);

  const resetToTorrent = (tObj: Torrent) => {
    setPriority(String(tObj.priority ?? 1));
    setUploadLimit(tObj.uploadLimit ?? 0);
    setDownloadLimit(tObj.downloadLimit ?? 0);
    setInitialSeeding(Boolean(tObj.initialSeeding));
    setForceStart(Boolean(tObj.forceStart));
    setSequentialDownload(Boolean(tObj.sequentialDownload));
    setIsPrivate(Boolean(tObj.isPrivate));
    setActive(
      tObj.active ?? (tObj.status !== "paused" && tObj.status !== "stopped"),
    );
    setLabel(tObj.label ?? "");
    setAnnounceInterval(tObj.announceInterval || 1800);
    setNextUpdate(tObj.nextUpdate || 1800);
    setThreshold(tObj.threshold || 1);
    setSmallTorrentLimit(tObj.smallTorrentLimit || 50);
    setTargetRatio(tObj.targetRatio ?? 0);
    setTargetSeedTimeMinutes(tObj.targetSeedTimeMinutes ?? 0);
    setShareLimitAction(tObj.shareLimitAction || "Default");
    setDirty(false);
  };

  useEffect(() => {
    if (torrent.id !== lastTorrentIdRef.current) {
      lastTorrentIdRef.current = torrent.id;
      resetToTorrent(torrent);
      return;
    }

    if (!dirty) {
      resetToTorrent(torrent);
    }
  }, [torrent, dirty]);

  const handleSave = () => {
    updateTorrent.mutate(
      {
        ...torrent,
        priority: parseInt(priority, 10),
        uploadLimit,
        downloadLimit,
        initialSeeding,
        forceStart,
        sequentialDownload,
        isPrivate,
        active,
        label: label ?? "",
        announceInterval,
        nextUpdate,
        threshold,
        smallTorrentLimit,
        targetRatio,
        targetSeedTimeMinutes,
        shareLimitAction,
      },
      { onSuccess: () => setDirty(false) },
    );
  };

  const handleReset = () => {
    resetToTorrent(torrent);
  };

  const mark =
    <T,>(setter: (v: T) => void) =>
    (v: T) => {
      setter(v);
      setDirty(true);
    };
  const numChange =
    (setter: (v: number) => void) => (e: React.ChangeEvent<HTMLInputElement>) =>
      mark(setter)(parseInt(e.target.value, 10) || 0);

  const priorityOptions = [
    { value: "0", label: t("torrents.detail.prioLow") },
    { value: "1", label: t("torrents.detail.prioNormal") },
    { value: "2", label: t("torrents.detail.prioHigh") },
  ];

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      {/* 3 Balanced Cards Grid */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(300px, 1fr))",
          gap: "0.75rem",
        }}
      >
        {/* Card 1: Transfer & Limits */}
        <div
          style={{
            backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            padding: "0.7rem 0.9rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.6rem",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 700,
              color: "var(--accent, #ffd166)",
              textTransform: "uppercase",
              letterSpacing: "0.05em",
              borderBottom:
                "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
              paddingBottom: "0.25rem",
            }}
          >
            {t("torrents.detail.transferLimits")}
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.queuePriority")}
            </label>
            <select
              className="form-select"
              value={priority}
              onChange={(e) => mark(setPriority)(e.target.value)}
              style={{
                width: "135px",
                padding: "0.25rem 0.5rem",
                fontSize: "0.78rem",
              }}
            >
              {priorityOptions.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <div>
              <div
                style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
              >
                {t("torrents.table.downloadLimit")}
              </div>
              <div style={{ fontSize: "0.68rem", color: "var(--text-muted)" }}>
                {t("torrents.detail.unlimitedHint")}
              </div>
            </div>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.4rem",
                width: "135px",
                justifyContent: "flex-end",
              }}
            >
              <input
                type="number"
                className="form-input"
                value={downloadLimit}
                onChange={numChange(setDownloadLimit)}
                min={0}
                style={{
                  width: "85px",
                  padding: "0.25rem 0.5rem",
                  fontSize: "0.78rem",
                  textAlign: "right",
                }}
              />
              <span
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted)",
                  width: "36px",
                  textAlign: "left",
                }}
              >
                KB/s
              </span>
            </div>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <div>
              <div
                style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
              >
                {t("torrents.table.uploadLimit")}
              </div>
              <div style={{ fontSize: "0.68rem", color: "var(--text-muted)" }}>
                {t("torrents.detail.unlimitedHint")}
              </div>
            </div>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.4rem",
                width: "135px",
                justifyContent: "flex-end",
              }}
            >
              <input
                type="number"
                className="form-input"
                value={uploadLimit}
                onChange={numChange(setUploadLimit)}
                min={0}
                style={{
                  width: "85px",
                  padding: "0.25rem 0.5rem",
                  fontSize: "0.78rem",
                  textAlign: "right",
                }}
              />
              <span
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted)",
                  width: "36px",
                  textAlign: "left",
                }}
              >
                KB/s
              </span>
            </div>
          </div>
        </div>

        {/* Card 2: Seeding & Queue Rules */}
        <div
          style={{
            backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            padding: "0.7rem 0.9rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.6rem",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 700,
              color: "var(--accent, #ffd166)",
              textTransform: "uppercase",
              letterSpacing: "0.05em",
              borderBottom:
                "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
              paddingBottom: "0.25rem",
            }}
          >
            {t("torrents.detail.seedingRules")}
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.activeSeeding")}
            </label>
            <label className="toggle-switch">
              <input
                type="checkbox"
                checked={active}
                onChange={(e) => mark(setActive)(e.target.checked)}
              />
              <span className="toggle-slider" />
            </label>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.superSeeding")}
            </label>
            <label className="toggle-switch">
              <input
                type="checkbox"
                checked={initialSeeding}
                onChange={(e) => mark(setInitialSeeding)(e.target.checked)}
              />
              <span className="toggle-slider" />
            </label>
          </div>

          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.2rem" }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.shareGoalAction")}
            </label>
            <select
              value={shareLimitAction}
              onChange={(e) => mark(setShareLimitAction)(e.target.value)}
              className="input-select"
              style={{ fontSize: "0.8rem", padding: "0.3rem 0.5rem" }}
            >
              <option value="Default">
                {t("torrents.detail.followGlobal")}
              </option>
              <option value="Pause">{t("torrents.detail.pauseSeeding")}</option>
              <option value="Remove">
                {t("torrents.detail.removeKeepData")}
              </option>
              <option value="RemoveWithData">
                {t("torrents.detail.removeDeleteData")}
              </option>
              <option value="SuperSeeding">
                {t("torrents.detail.switchSuperSeeding")}
              </option>
            </select>
          </div>

          <div style={{ display: "flex", gap: "0.5rem" }}>
            <div
              style={{
                flex: 1,
                display: "flex",
                flexDirection: "column",
                gap: "0.2rem",
              }}
            >
              <label
                style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
              >
                {t("torrents.detail.targetRatio")}
              </label>
              <input
                type="number"
                step="0.1"
                min="0"
                value={targetRatio}
                onChange={(e) =>
                  mark(setTargetRatio)(parseFloat(e.target.value) || 0)
                }
                className="input-text"
                style={{ fontSize: "0.8rem", padding: "0.3rem 0.5rem" }}
                placeholder="0 = global"
              />
            </div>
            <div
              style={{
                flex: 1,
                display: "flex",
                flexDirection: "column",
                gap: "0.2rem",
              }}
            >
              <label
                style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
              >
                {t("torrents.detail.targetSeedTime")}
              </label>
              <input
                type="number"
                min="0"
                value={targetSeedTimeMinutes}
                onChange={(e) =>
                  mark(setTargetSeedTimeMinutes)(
                    parseInt(e.target.value, 10) || 0,
                  )
                }
                className="input-text"
                style={{ fontSize: "0.8rem", padding: "0.3rem 0.5rem" }}
                placeholder="0 = global"
              />
            </div>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.forceStart")}
            </label>
            <label className="toggle-switch">
              <input
                type="checkbox"
                checked={forceStart}
                onChange={(e) => mark(setForceStart)(e.target.checked)}
              />
              <span className="toggle-slider" />
            </label>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.sequentialDownload")}
            </label>
            <label className="toggle-switch">
              <input
                type="checkbox"
                checked={sequentialDownload}
                onChange={(e) => mark(setSequentialDownload)(e.target.checked)}
              />
              <span className="toggle-slider" />
            </label>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <div>
              <label
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  display: "flex",
                  alignItems: "center",
                  gap: "5px",
                }}
              >
                <i
                  className="fas fa-lock"
                  style={{ color: "#f87171", fontSize: "0.75rem" }}
                />
                {t("torrents.detail.privateSwarmOption")}
              </label>
              <div style={{ fontSize: "0.68rem", color: "var(--text-muted)" }}>
                {t("torrents.detail.privateSwarmHint")}
              </div>
            </div>
            <label className="toggle-switch">
              <input
                type="checkbox"
                checked={isPrivate}
                onChange={(e) => mark(setIsPrivate)(e.target.checked)}
              />
              <span className="toggle-slider" />
            </label>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.label")}
            </label>
            <input
              type="text"
              className="form-input"
              value={label}
              onChange={(e) => mark(setLabel)(e.target.value)}
              placeholder={t("torrents.detail.labelPlaceholder")}
              style={{
                width: "135px",
                padding: "0.25rem 0.5rem",
                fontSize: "0.78rem",
              }}
            />
          </div>
        </div>

        {/* Card 3: Swarm & Tracker Timing */}
        <div
          style={{
            backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            padding: "0.7rem 0.9rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.6rem",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 700,
              color: "var(--accent, #ffd166)",
              textTransform: "uppercase",
              letterSpacing: "0.05em",
              borderBottom:
                "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
              paddingBottom: "0.25rem",
            }}
          >
            {t("torrents.detail.trackerTiming")}
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.announceInterval")}
            </label>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.4rem",
                width: "135px",
                justifyContent: "flex-end",
              }}
            >
              <input
                type="number"
                className="form-input"
                value={announceInterval}
                onChange={numChange(setAnnounceInterval)}
                min={0}
                style={{
                  width: "85px",
                  padding: "0.25rem 0.5rem",
                  fontSize: "0.78rem",
                  textAlign: "right",
                }}
              />
              <span
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted)",
                  width: "36px",
                  textAlign: "left",
                }}
              >
                sec
              </span>
            </div>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.nextUpdateIn", { seconds: nextUpdate })}
            </label>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.4rem",
                width: "135px",
                justifyContent: "flex-end",
              }}
            >
              <input
                type="number"
                className="form-input"
                value={nextUpdate}
                onChange={numChange(setNextUpdate)}
                min={0}
                style={{
                  width: "85px",
                  padding: "0.25rem 0.5rem",
                  fontSize: "0.78rem",
                  textAlign: "right",
                }}
              />
              <span
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted)",
                  width: "36px",
                  textAlign: "left",
                }}
              >
                sec
              </span>
            </div>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.availabilityThreshold")}
            </label>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.4rem",
                width: "135px",
                justifyContent: "flex-end",
              }}
            >
              <input
                type="number"
                className="form-input"
                value={threshold}
                onChange={numChange(setThreshold)}
                min={0}
                style={{
                  width: "85px",
                  padding: "0.25rem 0.5rem",
                  fontSize: "0.78rem",
                  textAlign: "right",
                }}
              />
              <span
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted)",
                  width: "36px",
                  textAlign: "left",
                }}
              >
                copies
              </span>
            </div>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <label
              style={{ fontSize: "0.8rem", color: "var(--text-secondary)" }}
            >
              {t("torrents.detail.smallTorrentLimit")}
            </label>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.4rem",
                width: "135px",
                justifyContent: "flex-end",
              }}
            >
              <input
                type="number"
                className="form-input"
                value={smallTorrentLimit}
                onChange={numChange(setSmallTorrentLimit)}
                min={0}
                style={{
                  width: "85px",
                  padding: "0.25rem 0.5rem",
                  fontSize: "0.78rem",
                  textAlign: "right",
                }}
              />
              <span
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted)",
                  width: "36px",
                  textAlign: "left",
                }}
              >
                MB
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Docked Action Bar */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          padding: "0.5rem 0.8rem",
          backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
          borderRadius: "6px",
          border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
        }}
      >
        <div
          style={{
            fontSize: "0.78rem",
            color: dirty ? "var(--warning, #f59e0b)" : "var(--text-muted)",
          }}
        >
          {dirty
            ? `● ${t("torrents.detail.unsavedChanges")}`
            : t("torrents.detail.inSync")}
        </div>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          {dirty && (
            <button
              type="button"
              className="btn btn-small"
              onClick={handleReset}
              disabled={updateTorrent.isPending}
            >
              {t("torrents.detail.reset")}
            </button>
          )}
          <button
            type="button"
            className="btn btn-success btn-small"
            onClick={handleSave}
            disabled={!dirty || updateTorrent.isPending}
          >
            {updateTorrent.isPending
              ? t("torrents.detail.saving")
              : t("torrents.detail.saveOptions")}
          </button>
        </div>
      </div>
    </div>
  );
}
