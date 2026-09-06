import { useTranslation } from "../i18n";
import { useUpdates } from "../api/hooks";

function CheckIcon() {
  const { t } = useTranslation();

  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.5"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="20 6 9 17 4 12" />
    </svg>
  );
}

function formatDate(iso: string): string {
  try {
    const d = new Date(iso);
    return isNaN(d.getTime())
      ? iso
      : d.toLocaleDateString(undefined, {
          year: "numeric",
          month: "long",
          day: "numeric",
        });
  } catch {
    return iso;
  }
}

function SystemUpdates() {
  const { t } = useTranslation();
  const { data: updates, isLoading, error } = useUpdates();

  const isUpToDate =
    updates &&
    updates.length > 0 &&
    updates.every((u) => !u.latest || u.installed);

  return (
    <div className="content-area">
      {/* Page Header */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.25rem",
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              {t("autogen.t_system_updates")}
            </h1>
            <span className="badge badge-primary">
              {t("autogen.t_releases")}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("autogen.t_software_version_history_changelogs_bug_")}
          </div>
        </div>
      </div>

      {isLoading && (
        <p className="loading">{t("autogen.t_checking_for_updates")}</p>
      )}

      {error && (
        <div className="card" style={{ marginBottom: "1rem" }}>
          <p className="error">{t("autogen.t_failed_to_check_for_updates")}</p>
        </div>
      )}

      {updates && (
        <>
          {/* Status Alert Banner */}
          <div
            className="card"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              padding: "1rem 1.25rem",
              marginBottom: "1.25rem",
              borderRadius: "8px",
              backgroundColor: isUpToDate
                ? "rgba(40, 167, 69, 0.12)"
                : "rgba(200, 168, 78, 0.12)",
              border: `1px solid ${
                isUpToDate
                  ? "rgba(40, 167, 69, 0.35)"
                  : "rgba(200, 168, 78, 0.35)"
              }`,
              color: isUpToDate
                ? "var(--success, #28a745)"
                : "var(--accent, #c8a84e)",
            }}
          >
            <span style={{ display: "flex", alignItems: "center" }}>
              <CheckIcon />
            </span>
            <div style={{ fontSize: "0.9rem", fontWeight: 600 }}>
              {isUpToDate
                ? "The latest version of Leecharr is already installed"
                : "A new version of Leecharr is available"}
            </div>
          </div>

          {/* Release History Cards */}
          <div
            style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
          >
            {updates.map((update) => (
              <div
                key={update.version}
                className="card"
                style={{
                  padding: "1.25rem 1.5rem",
                  borderRadius: "8px",
                  border: "1px solid rgba(255, 255, 255, 0.08)",
                  boxShadow:
                    "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.75rem",
                    marginBottom: "1rem",
                    borderBottom: "1px solid rgba(255, 255, 255, 0.06)",
                    paddingBottom: "0.75rem",
                  }}
                >
                  <span
                    style={{
                      fontSize: "1.1rem",
                      fontWeight: 700,
                      color: "var(--accent, #c8a84e)",
                    }}
                  >
                    v{update.version}
                  </span>
                  <span
                    style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}
                  >
                    📅 {formatDate(update.releaseDate)}
                  </span>
                  {update.installed && (
                    <span
                      className="badge badge-seeding"
                      style={{ marginLeft: "auto" }}
                    >
                      {t("autogen.t_currently_installed")}
                    </span>
                  )}
                  {update.latest && !update.installed && (
                    <span
                      className="badge badge-queued"
                      style={{ marginLeft: "auto" }}
                    >
                      {t("autogen.t_latest_release")}
                    </span>
                  )}
                </div>

                {update.changes &&
                  update.changes.new &&
                  update.changes.new.length > 0 && (
                    <div style={{ marginBottom: "0.85rem" }}>
                      <div
                        style={{
                          fontSize: "0.75rem",
                          fontWeight: 700,
                          textTransform: "uppercase",
                          letterSpacing: "0.05em",
                          color: "var(--success, #28a745)",
                          marginBottom: "0.4rem",
                        }}
                      >
                        {t("autogen.t_new_features")}
                      </div>
                      <ul
                        style={{
                          margin: 0,
                          paddingLeft: "1.25rem",
                          fontSize: "0.875rem",
                          color: "var(--text-secondary)",
                          lineHeight: 1.6,
                        }}
                      >
                        {update.changes.new.map((item, i) => (
                          <li key={i}>{item}</li>
                        ))}
                      </ul>
                    </div>
                  )}

                {update.changes &&
                  update.changes.fixed &&
                  update.changes.fixed.length > 0 && (
                    <div>
                      <div
                        style={{
                          fontSize: "0.75rem",
                          fontWeight: 700,
                          textTransform: "uppercase",
                          letterSpacing: "0.05em",
                          color: "var(--accent, #c8a84e)",
                          marginBottom: "0.4rem",
                        }}
                      >
                        {t("autogen.t_bug_fixes_improvements")}
                      </div>
                      <ul
                        style={{
                          margin: 0,
                          paddingLeft: "1.25rem",
                          fontSize: "0.875rem",
                          color: "var(--text-secondary)",
                          lineHeight: 1.6,
                        }}
                      >
                        {update.changes.fixed.map((item, i) => (
                          <li key={i}>{item}</li>
                        ))}
                      </ul>
                    </div>
                  )}
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}

export default SystemUpdates;
