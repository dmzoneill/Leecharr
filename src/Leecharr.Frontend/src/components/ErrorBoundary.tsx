import React, { Component, ErrorInfo, ReactNode } from "react";
import { ErrorIcon } from "./icons/UIIcons";

export interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode | ((error: Error, reset: () => void) => ReactNode);
  title?: string;
  onReset?: () => void;
  onError?: (error: Error, errorInfo: ErrorInfo) => void;
}

export interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
  errorInfo: ErrorInfo | null;
  showDetails: boolean;
}

/**
 * Reusable, dark-themed React Error Boundary component matching Leecharr's design palette.
 * Prevents unhandled rendering exceptions from crashing the entire application into a blank screen.
 */
export class ErrorBoundary extends Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  public override state: ErrorBoundaryState = {
    hasError: false,
    error: null,
    errorInfo: null,
    showDetails: false,
  };

  public static getDerivedStateFromError(
    error: Error,
  ): Partial<ErrorBoundaryState> {
    return { hasError: true, error };
  }

  public override componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    this.setState({ errorInfo });
    console.error(
      "ErrorBoundary caught an unhandled rendering error:",
      error,
      errorInfo,
    );
    this.props.onError?.(error, errorInfo);
  }

  public resetError = (): void => {
    this.setState({
      hasError: false,
      error: null,
      errorInfo: null,
      showDetails: false,
    });
    this.props.onReset?.();
  };

  private handleReload = (): void => {
    window.location.reload();
  };

  private toggleDetails = (): void => {
    this.setState((prev) => ({ showDetails: !prev.showDetails }));
  };

  public override render(): ReactNode {
    if (!this.state.hasError) {
      return this.props.children;
    }

    const { fallback, title = "Something went wrong" } = this.props;
    const { error, errorInfo, showDetails } = this.state;

    if (fallback) {
      if (typeof fallback === "function") {
        return fallback(error || new Error("Unknown error"), this.resetError);
      }
      return fallback;
    }

    const isDev = process.env.NODE_ENV === "development";

    return (
      <div
        className="error-boundary-container"
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          padding: "2rem",
          minHeight: "280px",
          width: "100%",
          boxSizing: "border-box",
        }}
      >
        <div
          className="error-boundary-card"
          style={{
            backgroundColor: "#171B35",
            border: "1px solid rgba(255, 209, 102, 0.25)",
            borderRadius: "10px",
            padding: "2rem",
            maxWidth: "680px",
            width: "100%",
            boxShadow: "0 8px 32px rgba(0, 0, 0, 0.45)",
            color: "#F8F4ED",
            display: "flex",
            flexDirection: "column",
            gap: "1.25rem",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.85rem",
              borderBottom: "1px solid rgba(255, 209, 102, 0.15)",
              paddingBottom: "1rem",
            }}
          >
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                width: "40px",
                height: "40px",
                borderRadius: "50%",
                backgroundColor: "rgba(239, 68, 68, 0.15)",
                color: "#ef4444",
                flexShrink: 0,
              }}
            >
              <ErrorIcon size={24} />
            </div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <h3
                style={{
                  margin: 0,
                  fontSize: "1.2rem",
                  fontWeight: 600,
                  color: "#FFD166",
                  letterSpacing: "0.02em",
                }}
              >
                {title}
              </h3>
              <p
                style={{
                  margin: "0.25rem 0 0",
                  fontSize: "0.85rem",
                  color: "rgba(248, 244, 237, 0.7)",
                }}
              >
                An unexpected error occurred while rendering this component.
              </p>
            </div>
          </div>

          {error && (
            <div
              style={{
                backgroundColor: "#10111A",
                border: "1px solid rgba(239, 68, 68, 0.3)",
                borderRadius: "6px",
                padding: "0.85rem 1rem",
                color: "#ff8b8b",
                fontFamily: "monospace",
                fontSize: "0.85rem",
                wordBreak: "break-word",
              }}
            >
              <strong>{error.name || "Error"}:</strong>{" "}
              {error.message || String(error)}
            </div>
          )}

          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              flexWrap: "wrap",
              gap: "0.75rem",
              marginTop: "0.5rem",
            }}
          >
            <div style={{ display: "flex", gap: "0.75rem" }}>
              <button
                type="button"
                className="btn btn-primary"
                onClick={this.resetError}
                style={{
                  backgroundColor: "#FFD166",
                  color: "#171B35",
                  fontWeight: 600,
                  border: "none",
                  padding: "0.5rem 1.1rem",
                  borderRadius: "6px",
                  cursor: "pointer",
                }}
              >
                Try Again
              </button>
              <button
                type="button"
                className="btn"
                onClick={this.handleReload}
                style={{
                  backgroundColor: "#23284B",
                  color: "#F8F4ED",
                  border: "1px solid rgba(255, 209, 102, 0.2)",
                  padding: "0.5rem 1.1rem",
                  borderRadius: "6px",
                  cursor: "pointer",
                }}
              >
                Reload Page
              </button>
            </div>

            {(error?.stack || errorInfo?.componentStack) && (
              <button
                type="button"
                className="btn"
                onClick={this.toggleDetails}
                style={{
                  backgroundColor: "transparent",
                  color: "rgba(248, 244, 237, 0.7)",
                  border: "1px dashed rgba(255, 209, 102, 0.3)",
                  fontSize: "0.78rem",
                  padding: "0.4rem 0.8rem",
                  borderRadius: "4px",
                  cursor: "pointer",
                }}
              >
                {showDetails ? "Hide Error Details" : "Show Error Details"}
              </button>
            )}
          </div>

          {(showDetails || isDev) &&
            (error?.stack || errorInfo?.componentStack) && (
              <div
                style={{
                  marginTop: "0.5rem",
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.5rem",
                }}
              >
                <div
                  style={{
                    fontSize: "0.75rem",
                    fontWeight: 600,
                    color: "#FFD166",
                    textTransform: "uppercase",
                    letterSpacing: "0.05em",
                  }}
                >
                  Diagnostic Stack Trace
                </div>
                <pre
                  style={{
                    backgroundColor: "#10111A",
                    border: "1px solid rgba(255, 255, 255, 0.1)",
                    borderRadius: "6px",
                    padding: "0.85rem",
                    margin: 0,
                    fontSize: "0.75rem",
                    color: "rgba(248, 244, 237, 0.85)",
                    fontFamily: "monospace",
                    overflowX: "auto",
                    maxHeight: "220px",
                    overflowY: "auto",
                    whiteSpace: "pre-wrap",
                    lineHeight: 1.45,
                  }}
                >
                  {error?.stack || ""}
                  {errorInfo?.componentStack
                    ? `\n\nComponent Stack:\n${errorInfo.componentStack}`
                    : ""}
                </pre>
              </div>
            )}
        </div>
      </div>
    );
  }
}

export default ErrorBoundary;
