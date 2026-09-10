import type { Torrent, TorrentFile, Category, SystemStatus } from "./types";

declare global {
  interface Window {
    Leecharr?: {
      urlBase?: string;
      apiKey?: string;
    };
  }
}

export function getUrlBase(): string {
  if (typeof window !== "undefined" && window.Leecharr?.urlBase) {
    return window.Leecharr.urlBase.replace(/\/+$/, "");
  }
  return "";
}

export const BASE_URL = `${getUrlBase()}/api/v1`;

async function parseResponseBody<T>(response: Response): Promise<T> {
  if (
    response.status === 204 ||
    response.headers.get("content-length") === "0"
  ) {
    return null as unknown as T;
  }
  const text = await response.text();
  if (!text || !text.trim()) {
    return null as unknown as T;
  }
  try {
    return JSON.parse(text) as T;
  } catch {
    return text as unknown as T;
  }
}

class ApiClient {
  private apiKey: string | null = null;

  constructor() {
    this.apiKey = localStorage.getItem("leecharr_apikey");
  }

  setApiKey(key: string) {
    this.apiKey = key;
    if (key) {
      localStorage.setItem("leecharr_apikey", key);
    } else {
      localStorage.removeItem("leecharr_apikey");
    }
  }

  getApiKey(): string | null {
    return this.apiKey || localStorage.getItem("leecharr_apikey");
  }

  async request<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
    const headers: Record<string, string> = {
      Accept: "application/json",
      ...(options.headers as Record<string, string>),
    };

    if (
      options.body &&
      typeof options.body === "string" &&
      !headers["Content-Type"]
    ) {
      headers["Content-Type"] = "application/json";
    }

    const key = this.getApiKey();
    if (key) {
      headers["X-Api-Key"] = key;
    }

    const url =
      endpoint.startsWith("http://") || endpoint.startsWith("https://")
        ? endpoint
        : endpoint.startsWith("/api/")
          ? `${getUrlBase()}${endpoint}`
          : endpoint.startsWith("/")
            ? `${BASE_URL}${endpoint}`
            : `${BASE_URL}/${endpoint}`;

    const response = await fetch(url, {
      ...options,
      headers,
    });

    if (!response.ok) {
      let message = `API error: ${response.status} ${response.statusText}`;
      let data: any = null;
      try {
        const text = await response.text();
        if (text) {
          data = text;
          try {
            const json = JSON.parse(text);
            data = json;
            message = json.message || json.title || text;
          } catch {
            message = text;
          }
        }
      } catch {
        // ignore
      }
      const error: any = new Error(message);
      error.status = response.status;
      error.response = {
        status: response.status,
        statusText: response.statusText,
        data,
      };
      throw error;
    }

    return parseResponseBody<T>(response);
  }

  get<T>(endpoint: string): Promise<T> {
    return this.request<T>(endpoint, { method: "GET" });
  }

  post<T>(endpoint: string, body?: unknown): Promise<T> {
    const isSpecialBody =
      body instanceof FormData || body instanceof URLSearchParams;
    return this.request<T>(endpoint, {
      method: "POST",
      headers:
        body !== undefined && !isSpecialBody
          ? { "Content-Type": "application/json" }
          : undefined,
      body:
        body !== undefined
          ? typeof body === "string" || isSpecialBody
            ? (body as BodyInit)
            : JSON.stringify(body)
          : undefined,
    });
  }

  postForm<T>(endpoint: string, formData: FormData): Promise<T> {
    return this.request<T>(endpoint, {
      method: "POST",
      body: formData,
    });
  }

  postUrlEncoded<T>(endpoint: string, params: URLSearchParams): Promise<T> {
    return this.request<T>(endpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded",
      },
      body: params.toString(),
    });
  }

  put<T>(endpoint: string, body?: unknown): Promise<T> {
    const isSpecialBody =
      body instanceof FormData || body instanceof URLSearchParams;
    return this.request<T>(endpoint, {
      method: "PUT",
      headers:
        body !== undefined && !isSpecialBody
          ? { "Content-Type": "application/json" }
          : undefined,
      body:
        body !== undefined
          ? typeof body === "string" || isSpecialBody
            ? (body as BodyInit)
            : JSON.stringify(body)
          : undefined,
    });
  }

  delete<T>(endpoint: string): Promise<T> {
    return this.request<T>(endpoint, { method: "DELETE" });
  }
}

export const apiClient = new ApiClient();

export const api = {
  // Torrents
  getTorrents: () => apiClient.get<Torrent[]>("/torrents"),
  getTorrent: (id: number) => apiClient.get<Torrent>(`/torrents/${id}`),
  getTorrentFiles: (id: number) =>
    apiClient.get<TorrentFile[]>(`/torrents/${id}/files`),
  pauseTorrent: (id: number) =>
    apiClient.post<Torrent>(`/torrents/${id}/pause`),
  resumeTorrent: (id: number) =>
    apiClient.post<Torrent>(`/torrents/${id}/resume`),
  recheckTorrent: (id: number) =>
    apiClient.post<Torrent>(`/torrents/${id}/recheck`),
  deleteTorrent: (id: number, deleteFiles = false) =>
    apiClient.delete<void>(`/torrents/${id}?deleteFiles=${deleteFiles}`),

  addTorrentMagnet: (
    magnetUrl: string,
    category = "",
    savePath = "",
    paused = false,
  ) => {
    const data = new FormData();
    data.append("magnetUrl", magnetUrl);
    if (category) data.append("category", category);
    if (savePath) data.append("savePath", savePath);
    if (paused) data.append("paused", "true");
    return apiClient.postForm<Torrent>("/torrents", data);
  },

  addTorrentFile: (
    file: File,
    category = "",
    savePath = "",
    paused = false,
  ) => {
    const data = new FormData();
    data.append("file", file);
    if (category) data.append("category", category);
    if (savePath) data.append("savePath", savePath);
    if (paused) data.append("paused", "true");
    return apiClient.postForm<Torrent>("/torrents", data);
  },

  // Categories
  getCategories: () => apiClient.get<Category[]>("/categories"),
  addCategory: (category: Partial<Category>) =>
    apiClient.post<Category>("/categories", category),
  updateCategory: (id: number, category: Partial<Category>) =>
    apiClient.put<Category>(`/categories/${id}`, category),
  deleteCategory: (id: number) => apiClient.delete<void>(`/categories/${id}`),

  // System
  getSystemStatus: () => apiClient.get<SystemStatus>("/system/status"),

  // Authentication & SSO
  getAuthProviders: () =>
    apiClient.get<import("./types").AuthProvider[]>("/auth/providers"),
  getCurrentUser: () =>
    apiClient.get<import("./types").CurrentUser>("/auth/me"),
  login: (credentials: {
    username: string;
    password: string;
    rememberMe?: boolean;
  }) =>
    apiClient.post<import("./types").CurrentUser>("/auth/login", credentials),
  logout: () => apiClient.post<{ message: string }>("/auth/logout"),

  // Identity Provider Config (Admin)
  getIdProviders: () =>
    apiClient.get<import("./types").IdentityProviderDefinition[]>(
      "/config/auth/providers",
    ),
  getIdProvider: (id: number) =>
    apiClient.get<import("./types").IdentityProviderDefinition>(
      `/config/auth/providers/${id}`,
    ),
  createIdProvider: (
    provider: Partial<import("./types").IdentityProviderDefinition>,
  ) =>
    apiClient.post<import("./types").IdentityProviderDefinition>(
      "/config/auth/providers",
      provider,
    ),
  updateIdProvider: (
    id: number,
    provider: Partial<import("./types").IdentityProviderDefinition>,
  ) =>
    apiClient.put<import("./types").IdentityProviderDefinition>(
      `/config/auth/providers/${id}`,
      provider,
    ),
  deleteIdProvider: (id: number) =>
    apiClient.delete<void>(`/config/auth/providers/${id}`),
  testIdProvider: (
    provider: Partial<import("./types").IdentityProviderDefinition>,
  ) =>
    apiClient.post<{ success: boolean; message: string }>(
      "/config/auth/providers/test",
      provider,
    ),
  testSsl: (request: import("./types").SslTestRequest) =>
    apiClient.post<import("./types").SslCertificateValidationResult>(
      "/config/general/test-ssl",
      request,
    ),
  getApiKey: () =>
    apiClient.get<import("./types").ApiKeyResource>("/config/general/api-key"),
  getSystemResources: () =>
    apiClient.get<import("./types").SystemResourceTelemetrySnapshot>(
      "/system/resources",
    ),
  getHostResources: () =>
    apiClient.get<import("./types").HostProcessResourceMetrics>(
      "/system/resources/host",
    ),
  getTorrentEngineMetrics: () =>
    apiClient.get<import("./types").TorrentEngineMetrics>(
      "/system/resources/engine",
    ),
  getPerTorrentMetrics: () =>
    apiClient.get<import("./types").TorrentResourceMetrics[]>(
      "/system/resources/torrents",
    ),
  getTorrentResourceMetrics: (id: number) =>
    apiClient.get<import("./types").TorrentResourceMetrics>(
      `/system/resources/torrents/${id}`,
    ),
  getSubsystemsTelemetry: () =>
    apiClient.get<import("./types").SubsystemTelemetryReport[]>(
      "/system/resources/subsystems",
    ),
  createTorrent: (request: import("./types").TorrentCreationRequest) =>
    apiClient.post<import("./types").TorrentCreationResult>(
      "/torrents/create",
      request,
    ),
  renameTorrentFile: (hash: string, oldPath: string, newPath: string) => {
    const params = new URLSearchParams();
    params.append("hash", hash);
    params.append("oldPath", oldPath);
    params.append("newPath", newPath);
    return apiClient.postUrlEncoded<void>(
      "/api/v2/torrents/renameFile",
      params,
    );
  },
  renameTorrentFolder: (hash: string, oldPath: string, newPath: string) => {
    const params = new URLSearchParams();
    params.append("hash", hash);
    params.append("oldPath", oldPath);
    params.append("newPath", newPath);
    return apiClient.postUrlEncoded<void>(
      "/api/v2/torrents/renameFolder",
      params,
    );
  },
};

export const configApi = {
  getApiKey: () => api.getApiKey(),
};
