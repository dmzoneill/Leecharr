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

  private async request<T>(
    endpoint: string,
    options: RequestInit = {},
  ): Promise<T> {
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
      throw new Error(`API error: ${response.status} ${response.statusText}`);
    }

    if (
      response.status === 204 ||
      response.headers.get("content-length") === "0"
    ) {
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

  if (
    response.status === 204 ||
    response.headers.get("content-length") === "0"
  ) {
    return null as unknown as T;
  }

  return response.json();
}

export const api = {
  // Torrents
  getTorrents: () => fetchJson<Torrent[]>(`${BASE_URL}/torrents`),
  getTorrent: (id: number) => fetchJson<Torrent>(`${BASE_URL}/torrents/${id}`),
  getTorrentFiles: (id: number) =>
    fetchJson<TorrentFile[]>(`${BASE_URL}/torrents/${id}/files`),
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
    return fetchJson<Torrent>(`${BASE_URL}/torrents`, {
      method: "POST",
      body: data,
    });
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
  getAuthProviders: () =>
    fetchJson<import("./types").AuthProvider[]>(`${BASE_URL}/auth/providers`),
  getCurrentUser: () =>
    fetchJson<import("./types").CurrentUser>(`${BASE_URL}/auth/me`),
  login: (credentials: {
    username: string;
    password: string;
    rememberMe?: boolean;
  }) =>
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
    fetchJson<import("./types").IdentityProviderDefinition[]>(
      `${BASE_URL}/config/auth/providers`,
    ),
  getIdProvider: (id: number) =>
    fetchJson<import("./types").IdentityProviderDefinition>(
      `${BASE_URL}/config/auth/providers/${id}`,
    ),
  createIdProvider: (
    provider: Partial<import("./types").IdentityProviderDefinition>,
  ) =>
    fetchJson<import("./types").IdentityProviderDefinition>(
      `${BASE_URL}/config/auth/providers`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(provider),
      },
    ),
  updateIdProvider: (
    id: number,
    provider: Partial<import("./types").IdentityProviderDefinition>,
  ) =>
    fetchJson<import("./types").IdentityProviderDefinition>(
      `${BASE_URL}/config/auth/providers/${id}`,
      {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(provider),
      },
    ),
  deleteIdProvider: (id: number) =>
    fetchJson<void>(`${BASE_URL}/config/auth/providers/${id}`, {
      method: "DELETE",
    }),
  testIdProvider: (
    provider: Partial<import("./types").IdentityProviderDefinition>,
  ) =>
    fetchJson<{ success: boolean; message: string }>(
      `${BASE_URL}/config/auth/providers/test`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(provider),
      },
    ),
  testSsl: (request: import("./types").SslTestRequest) =>
    fetchJson<import("./types").SslCertificateValidationResult>(
      `${BASE_URL}/config/general/test-ssl`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
      },
    ),
};
