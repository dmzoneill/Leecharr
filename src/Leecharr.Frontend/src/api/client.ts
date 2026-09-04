import { Torrent, TorrentFile, Category, SystemStatus } from "./types";

const BASE_URL = "/api/v1";

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

  private async request<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
    const headers: HeadersInit = {
      "Content-Type": "application/json",
      Accept: "application/json",
      ...options.headers,
    };

    const key = this.getApiKey();
    if (key) {
      (headers as Record<string, string>)["X-Api-Key"] = key;
    }

    const response = await fetch(`${BASE_URL}${endpoint}`, {
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
      error.response = { status: response.status, statusText: response.statusText, data };
      throw error;
    }

    if (response.status === 204 || response.headers.get("content-length") === "0") {
      return null as unknown as T;
    }

    return response.json();
  }

  get<T>(endpoint: string): Promise<T> {
    return this.request<T>(endpoint, { method: "GET" });
  }

  post<T>(endpoint: string, body?: unknown): Promise<T> {
    return this.request<T>(endpoint, {
      method: "POST",
      body: body ? JSON.stringify(body) : undefined,
    });
  }

  async postForm<T>(endpoint: string, formData: FormData): Promise<T> {
    const headers: HeadersInit = {
      Accept: "application/json",
    };

    if (this.apiKey) {
      (headers as Record<string, string>)["X-Api-Key"] = this.apiKey;
    }

    const response = await fetch(`${BASE_URL}${endpoint}`, {
      method: "POST",
      headers,
      body: formData,
    });

    if (!response.ok) {
      throw new Error(`API error: ${response.status} ${response.statusText}`);
    }

    return response.json();
  }

  put<T>(endpoint: string, body?: unknown): Promise<T> {
    return this.request<T>(endpoint, {
      method: "PUT",
      body: body ? JSON.stringify(body) : undefined,
    });
  }

  delete<T>(endpoint: string): Promise<T> {
    return this.request<T>(endpoint, { method: "DELETE" });
  }
}

export const apiClient = new ApiClient();

async function fetchJson<T>(url: string, options?: RequestInit): Promise<T> {
  const apiKey = apiClient.getApiKey();
  const headers: Record<string, string> = {
    Accept: "application/json",
    ...(options?.headers as Record<string, string>),
  };

  if (apiKey) {
    headers["X-Api-Key"] = apiKey;
  }

  const response = await fetch(url, {
    ...options,
    headers,
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => response.statusText);
    throw new Error(errorText || `HTTP Error ${response.status}`);
  }

  if (response.status === 204 || response.headers.get("content-length") === "0") {
    return null as unknown as T;
  }

  return response.json();
}

export const api = {
  // Torrents
  getTorrents: () => fetchJson<Torrent[]>(`${BASE_URL}/torrents`),
  getTorrent: (id: number) => fetchJson<Torrent>(`${BASE_URL}/torrents/${id}`),
  getTorrentFiles: (id: number) => fetchJson<TorrentFile[]>(`${BASE_URL}/torrents/${id}/files`),
  pauseTorrent: (id: number) =>
    fetchJson<void>(`${BASE_URL}/torrents/${id}/pause`, { method: "POST" }),
  resumeTorrent: (id: number) =>
    fetchJson<void>(`${BASE_URL}/torrents/${id}/resume`, { method: "POST" }),
  recheckTorrent: (id: number) =>
    fetchJson<void>(`${BASE_URL}/torrents/${id}/recheck`, { method: "POST" }),
  deleteTorrent: (id: number, deleteFiles = false) =>
    fetchJson<void>(`${BASE_URL}/torrents/${id}?deleteFiles=${deleteFiles}`, {
      method: "DELETE",
    }),

  addTorrentMagnet: (magnetUrl: string, category = "", savePath = "", paused = false) => {
    const data = new FormData();
    data.append("magnetUrl", magnetUrl);
    if (category) data.append("category", category);
    if (savePath) data.append("savePath", savePath);
    if (paused) data.append("paused", "true");
    return fetchJson<Torrent>(`${BASE_URL}/torrents`, {
      method: "POST",
      body: data,
    });
  },

  addTorrentFile: (file: File, category = "", savePath = "", paused = false) => {
    const data = new FormData();
    data.append("file", file);
    if (category) data.append("category", category);
    if (savePath) data.append("savePath", savePath);
    if (paused) data.append("paused", "true");
    return fetchJson<Torrent>(`${BASE_URL}/torrents`, {
      method: "POST",
      body: data,
    });
  },

  // Categories
  getCategories: () => fetchJson<Category[]>(`${BASE_URL}/categories`),
  addCategory: (category: Partial<Category>) =>
    fetchJson<Category>(`${BASE_URL}/categories`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(category),
    }),
  deleteCategory: (id: number) =>
    fetchJson<void>(`${BASE_URL}/categories/${id}`, { method: "DELETE" }),

  // System
  getSystemStatus: () => fetchJson<SystemStatus>(`${BASE_URL}/system/status`),

  // Authentication & SSO
  getAuthProviders: () => fetchJson<import("./types").AuthProvider[]>(`${BASE_URL}/auth/providers`),
  getCurrentUser: () => fetchJson<import("./types").CurrentUser>(`${BASE_URL}/auth/me`),
  login: (credentials: { username: string; password: string; rememberMe?: boolean }) =>
    fetchJson<import("./types").CurrentUser>(`${BASE_URL}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(credentials),
    }),
  logout: () =>
    fetchJson<{ message: string }>(`${BASE_URL}/auth/logout`, {
      method: "POST",
    }),

  // Identity Provider Config (Admin)
  getIdProviders: () =>
    fetchJson<import("./types").IdentityProviderDefinition[]>(`${BASE_URL}/config/auth/providers`),
  getIdProvider: (id: number) =>
    fetchJson<import("./types").IdentityProviderDefinition>(
      `${BASE_URL}/config/auth/providers/${id}`
    ),
  createIdProvider: (provider: Partial<import("./types").IdentityProviderDefinition>) =>
    fetchJson<import("./types").IdentityProviderDefinition>(`${BASE_URL}/config/auth/providers`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(provider),
    }),
  updateIdProvider: (id: number, provider: Partial<import("./types").IdentityProviderDefinition>) =>
    fetchJson<import("./types").IdentityProviderDefinition>(
      `${BASE_URL}/config/auth/providers/${id}`,
      {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(provider),
      }
    ),
  deleteIdProvider: (id: number) =>
    fetchJson<void>(`${BASE_URL}/config/auth/providers/${id}`, {
      method: "DELETE",
    }),
  testIdProvider: (provider: Partial<import("./types").IdentityProviderDefinition>) =>
    fetchJson<{ success: boolean; message: string }>(`${BASE_URL}/config/auth/providers/test`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(provider),
    }),
  testSsl: (request: import("./types").SslTestRequest) =>
    fetchJson<import("./types").SslCertificateValidationResult>(
      `${BASE_URL}/config/general/test-ssl`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
      }
    ),
  getApiKey: () =>
    fetchJson<import("./types").ApiKeyResource>(`${BASE_URL}/config/general/api-key`),
  getSystemResources: () =>
    fetchJson<import("./types").SystemResourceTelemetrySnapshot>(`${BASE_URL}/system/resources`),
  getHostResources: () =>
    fetchJson<import("./types").HostProcessResourceMetrics>(`${BASE_URL}/system/resources/host`),
  getTorrentEngineMetrics: () =>
    fetchJson<import("./types").TorrentEngineMetrics>(`${BASE_URL}/system/resources/engine`),
  getPerTorrentMetrics: () =>
    fetchJson<import("./types").TorrentResourceMetrics[]>(`${BASE_URL}/system/resources/torrents`),
  getTorrentResourceMetrics: (id: number) =>
    fetchJson<import("./types").TorrentResourceMetrics>(
      `${BASE_URL}/system/resources/torrents/${id}`
    ),
  getSubsystemsTelemetry: () =>
    fetchJson<import("./types").SubsystemTelemetryReport[]>(
      `${BASE_URL}/system/resources/subsystems`
    ),
  createTorrent: (request: import("./types").TorrentCreationRequest) =>
    fetchJson<import("./types").TorrentCreationResult>(`/api/v2/torrents/create`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }),
  renameTorrentFile: async (hash: string, oldPath: string, newPath: string) => {
    const params = new URLSearchParams();
    params.append("hash", hash);
    params.append("oldPath", oldPath);
    params.append("newPath", newPath);
    const headers: HeadersInit = { "Content-Type": "application/x-www-form-urlencoded" };
    const key = apiClient.getApiKey();
    if (key) {
      (headers as Record<string, string>)["X-Api-Key"] = key;
    }
    const response = await fetch(`/api/v2/torrents/renameFile`, {
      method: "POST",
      headers,
      body: params.toString(),
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `Failed to rename (${response.status})`);
    }
    return response;
  },
  renameTorrentFolder: async (hash: string, oldPath: string, newPath: string) => {
    const params = new URLSearchParams();
    params.append("hash", hash);
    params.append("oldPath", oldPath);
    params.append("newPath", newPath);
    const headers: HeadersInit = { "Content-Type": "application/x-www-form-urlencoded" };
    const key = apiClient.getApiKey();
    if (key) {
      (headers as Record<string, string>)["X-Api-Key"] = key;
    }
    const response = await fetch(`/api/v2/torrents/renameFolder`, {
      method: "POST",
      headers,
      body: params.toString(),
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `Failed to rename (${response.status})`);
    }
    return response;
  },
};

export const configApi = {
  getApiKey: () => api.getApiKey(),
};
