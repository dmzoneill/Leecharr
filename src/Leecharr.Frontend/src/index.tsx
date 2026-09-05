import React from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ToastProvider } from "./context/ToastContext";
import { ThemeProvider } from "./context/ThemeContext";
import { ConfirmProvider } from "./context/ConfirmContext";
import { SettingsDirtyProvider } from "./pages/settings/SettingsDirtyContext";
import { ErrorBoundary } from "./components/ErrorBoundary";
import App from "./App";
import "./App.css";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});

const container = document.getElementById("root");
if (!container) {
  throw new Error("Root element not found");
}

const urlBase =
  typeof window !== "undefined" && (window as any).Leecharr?.urlBase
    ? (window as any).Leecharr.urlBase.replace(/\/+$/, "")
    : "";

const root = createRoot(container);
root.render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter basename={urlBase || undefined}>
        <ThemeProvider>
          <ToastProvider>
            <ConfirmProvider>
              <SettingsDirtyProvider>
                <ErrorBoundary title="Application Error">
                  <App />
                </ErrorBoundary>
              </SettingsDirtyProvider>
            </ConfirmProvider>
          </ToastProvider>
        </ThemeProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>
);
