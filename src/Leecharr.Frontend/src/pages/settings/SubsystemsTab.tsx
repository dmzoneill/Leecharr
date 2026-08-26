import React, { useState } from "react";
import {
  useSubsystems,
  useSwitchSubsystem,
  useProbeSubsystemProvider,
} from "../../api/hooks";
import { useToast } from "../../context/ToastContext";
import type {
  SubsystemOverview,
  SubsystemProvider,
  SubsystemProbeResult,
} from "../../api/types";
import { SectionCard } from "./shared";

export function SubsystemsTab() {
  const { showToast } = useToast();
  const {
    data: subsystems,
    isLoading,
    isError,
    error,
    refetch,
  } = useSubsystems();
  const switchSubsystem = useSwitchSubsystem();
  const probeProvider = useProbeSubsystemProvider();

  const [selectedForSwitch, setSelectedForSwitch] = useState<{
    subsystem: SubsystemOverview;
    provider: SubsystemProvider;
  } | null>(null);

  const [probeResult, setProbeResult] = useState<SubsystemProbeResult | null>(
    null,
  );
  const [probeLoadingKey, setProbeLoadingKey] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [activeCategoryFilter, setActiveCategoryFilter] =
    useState<string>("all");

  const handleProbe = async (subsystemId: string, providerId: string) => {
    const key = `${subsystemId}:${providerId}`;
    setProbeLoadingKey(key);
    try {
      const res = await probeProvider.mutateAsync({ subsystemId, providerId });
      setProbeResult(res);
    } catch (err: any) {
      showToast(`Probe failed: ${err.message || "Unknown error"}`, "error");
    } finally {
      setProbeLoadingKey(null);
    }
  };

  const handleExecuteSwitch = async () => {
    if (!selectedForSwitch) return;
    try {
      const res = await switchSubsystem.mutateAsync({
        subsystemId: selectedForSwitch.subsystem.id,
        providerId: selectedForSwitch.provider.providerId,
      });

      if (res.success) {
        showToast(
          res.message ||
            `Successfully switched ${selectedForSwitch.subsystem.name} provider to ${selectedForSwitch.provider.displayName}.`,
          "success",
        );
        setSelectedForSwitch(null);
      } else {
        showToast(
          `Failed to switch provider: ${res.error || "Unknown error"}`,
          "error",
        );
      }
    } catch (err: any) {
      showToast(`Failed to switch provider: ${err.message || "Unknown error"}`, "error");
    }
  };

  if (isLoading) {
    return <div className="loading">Loading pluggable subsystems...</div>;
  }

  if (isError) {
    return (
      <div style={{ color: "#e74c3c", padding: "1rem" }}>
        Error loading subsystems: {(error as Error)?.message}
        <button
          onClick={() => refetch()}
          style={{ marginLeft: "1rem", padding: "0.25rem 0.75rem" }}
        >
          Retry
        </button>
      </div>
    );
  }

  const allCategories = [
    "all",
    ...Array.from(new Set(subsystems?.map((s) => s.category) || [])),
  ];

  const filteredSubsystems =
    activeCategoryFilter === "all"
      ? subsystems
      : subsystems?.filter((s) => s.category === activeCategoryFilter);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "1.5rem" }}>
      {/* Overview Banner */}
      <div
        style={{
          backgroundColor: "var(--bg-secondary, #171b35)",
          border: "1px solid var(--border-color, #23284b)",
          borderRadius: "8px",
          padding: "1.25rem 1.5rem",
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h2
            style={{
              margin: "0 0 0.25rem 0",
              color: "var(--text-primary, #f8f4ed)",
              fontSize: "1.25rem",
            }}
          >
            Pluggable Architecture & Subsystem Hot-Swapping
          </h2>
          <p
            style={{
              margin: 0,
              color: "var(--text-secondary, #c7c5d3)",
              fontSize: "0.9rem",
            }}
          >
            Switch underlying engines, inspectors, geolocation resolvers, and
            security layers at runtime with zero downtime.
          </p>
        </div>

        {/* Category Filter Chips */}
        <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
          {allCategories.map((cat) => (
            <button
              key={cat}
              onClick={() => setActiveCategoryFilter(cat)}
              style={{
                padding: "0.35rem 0.85rem",
                borderRadius: "20px",
                border: "1px solid",
                borderColor:
                  activeCategoryFilter === cat
                    ? "var(--accent-gold, #ffd166)"
                    : "var(--border-color, #23284b)",
                backgroundColor:
                  activeCategoryFilter === cat
                    ? "rgba(255, 209, 102, 0.15)"
                    : "var(--bg-primary, #10111a)",
                color:
                  activeCategoryFilter === cat
                    ? "var(--accent-gold, #ffd166)"
                    : "var(--text-secondary, #c7c5d3)",
                fontSize: "0.85rem",
                fontWeight: activeCategoryFilter === cat ? 600 : 400,
                cursor: "pointer",
                textTransform: cat === "all" ? "capitalize" : "none",
                transition: "all 0.2s ease",
              }}
            >
              {cat === "all" ? "All Subsystems" : cat}
            </button>
          ))}
        </div>
      </div>

      {/* Success Notification */}
      {successMessage && (
        <div
          style={{
            backgroundColor: "rgba(39, 174, 96, 0.15)",
            border: "1px solid #27ae60",
            borderRadius: "6px",
            padding: "0.75rem 1rem",
            color: "#2ecc71",
            fontWeight: 600,
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
          }}
        >
          <span>✓</span> {successMessage}
        </div>
      )}

      {/* Subsystem Cards */}
      {filteredSubsystems?.map((subsystem) => (
        <SectionCard
          key={subsystem.id}
          title={`${subsystem.name} (${subsystem.category})`}
          description={subsystem.description}
        >
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))",
              gap: "1rem",
              marginTop: "0.5rem",
            }}
          >
            {subsystem.providers.map((provider) => {
              const probeKey = `${subsystem.id}:${provider.providerId}`;
              const isProbing = probeLoadingKey === probeKey;

              return (
                <div
                  key={provider.providerId}
                  style={{
                    backgroundColor: provider.isActive
                      ? "rgba(255, 209, 102, 0.04)"
                      : "var(--bg-primary, #10111a)",
                    border: `1.5px solid ${
                      provider.isActive
                        ? "var(--accent-gold, #ffd166)"
                        : "var(--border-color, #23284b)"
                    }`,
                    borderRadius: "8px",
                    padding: "1rem",
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    gap: "0.75rem",
                    position: "relative",
                  }}
                >
                  {/* Card Header */}
                  <div>
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "flex-start",
                        marginBottom: "0.35rem",
                      }}
                    >
                      <strong
                        style={{
                          fontSize: "1rem",
                          color: "var(--text-primary, #f8f4ed)",
                        }}
                      >
                        {provider.displayName}
                      </strong>
                      <div style={{ display: "flex", gap: "0.35rem" }}>
                        {provider.isActive ? (
                          <span
                            style={{
                              backgroundColor: "#27ae60",
                              color: "#ffffff",
                              padding: "0.15rem 0.5rem",
                              borderRadius: "4px",
                              fontSize: "0.72rem",
                              fontWeight: 700,
                              letterSpacing: "0.03em",
                            }}
                          >
                            ACTIVE
                          </span>
                        ) : provider.isAvailable ? (
                          <span
                            style={{
                              backgroundColor: "#2980b9",
                              color: "#ffffff",
                              padding: "0.15rem 0.5rem",
                              borderRadius: "4px",
                              fontSize: "0.72rem",
                              fontWeight: 600,
                            }}
                          >
                            READY
                          </span>
                        ) : (
                          <span
                            style={{
                              backgroundColor: "#7f8c8d",
                              color: "#ffffff",
                              padding: "0.15rem 0.5rem",
                              borderRadius: "4px",
                              fontSize: "0.72rem",
                              fontWeight: 600,
                            }}
                          >
                            EMULATED
                          </span>
                        )}
                      </div>
                    </div>

                    <div
                      style={{
                        fontSize: "0.8rem",
                        color: "var(--text-secondary, #c7c5d3)",
                        marginBottom: "0.5rem",
                      }}
                    >
                      v{provider.version} &bull; ID:{" "}
                      <code>{provider.providerId}</code>
                    </div>

                    <p
                      style={{
                        fontSize: "0.83rem",
                        color: "var(--text-secondary, #c7c5d3)",
                        margin: "0 0 0.75rem 0",
                        lineHeight: 1.4,
                      }}
                    >
                      {provider.description}
                    </p>

                    {/* Capabilities Badges */}
                    {provider.capabilities &&
                      Object.keys(provider.capabilities).length > 0 && (
                        <div
                          style={{
                            display: "flex",
                            flexWrap: "wrap",
                            gap: "0.35rem",
                            marginBottom: "0.75rem",
                          }}
                        >
                          {Object.entries(provider.capabilities).map(
                            ([k, v]) => {
                              if (typeof v === "boolean" && !v) return null;
                              const label = k
                                .replace(/^supports/, "")
                                .replace(/([A-Z])/g, " $1")
                                .trim();

                              return (
                                <span
                                  key={k}
                                  style={{
                                    backgroundColor:
                                      "var(--bg-card-hover, #23284b)",
                                    color: "var(--text-secondary, #c7c5d3)",
                                    padding: "0.15rem 0.45rem",
                                    borderRadius: "3px",
                                    fontSize: "0.7rem",
                                  }}
                                >
                                  {label}
                                </span>
                              );
                            },
                          )}
                        </div>
                      )}
                  </div>

                  {/* Action Buttons */}
                  <div
                    style={{
                      display: "flex",
                      gap: "0.5rem",
                      borderTop: "1px solid var(--border-color, #23284b)",
                      paddingTop: "0.75rem",
                    }}
                  >
                    <button
                      type="button"
                      disabled={isProbing}
                      onClick={() =>
                        handleProbe(subsystem.id, provider.providerId)
                      }
                      style={{
                        flex: 1,
                        padding: "0.4rem 0.6rem",
                        fontSize: "0.8rem",
                        backgroundColor: "var(--bg-card-hover, #23284b)",
                        border: "1px solid var(--border-color, #23284b)",
                        color: "var(--text-primary, #f8f4ed)",
                        borderRadius: "4px",
                        cursor: "pointer",
                      }}
                    >
                      {isProbing ? "Probing..." : "🔍 Test / Probe"}
                    </button>

                    {!provider.isActive && (
                      <button
                        type="button"
                        onClick={() =>
                          setSelectedForSwitch({ subsystem, provider })
                        }
                        style={{
                          flex: 1,
                          padding: "0.4rem 0.6rem",
                          fontSize: "0.8rem",
                          backgroundColor: "var(--accent-gold, #ffd166)",
                          border: "none",
                          color: "#10111a",
                          fontWeight: 600,
                          borderRadius: "4px",
                          cursor: "pointer",
                        }}
                      >
                        ⚡ Switch
                      </button>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        </SectionCard>
      ))}

      {/* Switch Confirmation Modal */}
      {selectedForSwitch && (
        <div
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0,0,0,0.75)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
          }}
        >
          <div
            style={{
              backgroundColor: "var(--bg-secondary, #171b35)",
              border: "1px solid var(--border-color, #23284b)",
              borderRadius: "8px",
              padding: "1.5rem",
              maxWidth: "500px",
              width: "90%",
            }}
          >
            <h3
              style={{
                margin: "0 0 0.75rem 0",
                color: "var(--text-primary, #f8f4ed)",
              }}
            >
              Hot-Swap Subsystem Provider
            </h3>

            <p
              style={{
                color: "var(--text-secondary, #c7c5d3)",
                fontSize: "0.9rem",
              }}
            >
              Are you sure you want to switch{" "}
              <strong>{selectedForSwitch.subsystem.name}</strong> to{" "}
              <strong style={{ color: "var(--accent-gold, #ffd166)" }}>
                {selectedForSwitch.provider.displayName}
              </strong>
              ?
            </p>

            <p
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                backgroundColor: "var(--bg-primary, #10111a)",
                padding: "0.75rem",
                borderRadius: "4px",
                border: "1px solid var(--border-color, #23284b)",
              }}
            >
              ℹ️ Provider switching executes atomically with zero application
              restart. Active workloads will seamlessly migrate to the new
              provider.
            </p>

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.75rem",
                marginTop: "1.25rem",
              }}
            >
              <button
                type="button"
                onClick={() => setSelectedForSwitch(null)}
                style={{
                  padding: "0.5rem 1rem",
                  backgroundColor: "transparent",
                  border: "1px solid var(--border-color, #23284b)",
                  color: "var(--text-primary, #f8f4ed)",
                  borderRadius: "4px",
                  cursor: "pointer",
                }}
              >
                Cancel
              </button>
              <button
                type="button"
                disabled={switchSubsystem.isPending}
                onClick={handleExecuteSwitch}
                style={{
                  padding: "0.5rem 1.25rem",
                  backgroundColor: "var(--accent-gold, #ffd166)",
                  border: "none",
                  color: "#10111a",
                  fontWeight: 700,
                  borderRadius: "4px",
                  cursor: "pointer",
                }}
              >
                {switchSubsystem.isPending
                  ? "Switching..."
                  : "Confirm Hot-Swap"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Diagnostics / Health Probe Result Modal */}
      {probeResult && (
        <div
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0,0,0,0.75)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
          }}
        >
          <div
            style={{
              backgroundColor: "var(--bg-secondary, #171b35)",
              border: "1px solid var(--border-color, #23284b)",
              borderRadius: "8px",
              padding: "1.5rem",
              maxWidth: "520px",
              width: "90%",
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "1rem",
              }}
            >
              <h3 style={{ margin: 0, color: "var(--text-primary, #f8f4ed)" }}>
                Provider Diagnostic Probe
              </h3>
              <span
                style={{
                  backgroundColor: probeResult.isHealthy
                    ? "#27ae60"
                    : "#e74c3c",
                  color: "#ffffff",
                  padding: "0.2rem 0.5rem",
                  borderRadius: "4px",
                  fontSize: "0.75rem",
                  fontWeight: 700,
                }}
              >
                {probeResult.isHealthy
                  ? "HEALTHY / READY"
                  : "WARNING / UNHEALTHY"}
              </span>
            </div>

            <p
              style={{
                color: "var(--text-secondary, #c7c5d3)",
                fontSize: "0.9rem",
              }}
            >
              <strong>Status:</strong> {probeResult.statusMessage}
            </p>

            {probeResult.dependencyChecks &&
              probeResult.dependencyChecks.length > 0 && (
                <div style={{ marginTop: "0.75rem" }}>
                  <span
                    style={{
                      fontSize: "0.8rem",
                      color: "var(--text-secondary, #c7c5d3)",
                      fontWeight: 600,
                    }}
                  >
                    Dependency Verification:
                  </span>
                  <ul
                    style={{
                      margin: "0.35rem 0 0 0",
                      paddingLeft: "1.2rem",
                      fontSize: "0.82rem",
                      color: "var(--text-secondary, #c7c5d3)",
                    }}
                  >
                    {probeResult.dependencyChecks.map((dep, idx) => (
                      <li key={idx} style={{ color: "#2ecc71" }}>
                        {dep}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

            {probeResult.warnings && probeResult.warnings.length > 0 && (
              <div style={{ marginTop: "0.75rem" }}>
                <span
                  style={{
                    fontSize: "0.8rem",
                    color: "#f39c12",
                    fontWeight: 600,
                  }}
                >
                  Diagnostics & Warnings:
                </span>
                <ul
                  style={{
                    margin: "0.35rem 0 0 0",
                    paddingLeft: "1.2rem",
                    fontSize: "0.82rem",
                    color: "#f39c12",
                  }}
                >
                  {probeResult.warnings.map((warn, idx) => (
                    <li key={idx}>{warn}</li>
                  ))}
                </ul>
              </div>
            )}

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                marginTop: "1.25rem",
              }}
            >
              <button
                type="button"
                onClick={() => setProbeResult(null)}
                style={{
                  padding: "0.5rem 1.25rem",
                  backgroundColor: "var(--bg-card-hover, #23284b)",
                  border: "1px solid var(--border-color, #23284b)",
                  color: "var(--text-primary, #f8f4ed)",
                  borderRadius: "4px",
                  cursor: "pointer",
                }}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
