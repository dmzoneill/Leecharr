import { Torrent, TorrentFile, Category, SystemStatus } from './types';

const BASE_URL = '/api/v1';

async function fetchJson<T>(url: string, options?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    ...options,
    headers: {
      'Accept': 'application/json',
      ...options?.headers,
    },
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => response.statusText);
    throw new Error(errorText || `HTTP Error ${response.status}`);
  }

  return response.json();
}

export const api = {
  // Torrents
  getTorrents: () => fetchJson<Torrent[]>(`${BASE_URL}/torrents`),
  getTorrent: (id: number) => fetchJson<Torrent>(`${BASE_URL}/torrents/${id}`),
  getTorrentFiles: (id: number) => fetchJson<TorrentFile[]>(`${BASE_URL}/torrents/${id}/files`),
  pauseTorrent: (id: number) => fetch(`${BASE_URL}/torrents/${id}/pause`, { method: 'POST' }),
  resumeTorrent: (id: number) => fetch(`${BASE_URL}/torrents/${id}/resume`, { method: 'POST' }),
  recheckTorrent: (id: number) => fetch(`${BASE_URL}/torrents/${id}/recheck`, { method: 'POST' }),
  deleteTorrent: (id: number, deleteFiles = false) =>
    fetch(`${BASE_URL}/torrents/${id}?deleteFiles=${deleteFiles}`, { method: 'DELETE' }),

  addTorrentMagnet: (magnetUrl: string, category = '', savePath = '', paused = false) => {
    const data = new FormData();
    data.append('magnetUrl', magnetUrl);
    if (category) data.append('category', category);
    if (savePath) data.append('savePath', savePath);
    if (paused) data.append('paused', 'true');
    return fetchJson<Torrent>(`${BASE_URL}/torrents`, { method: 'POST', body: data });
  },

  addTorrentFile: (file: File, category = '', savePath = '', paused = false) => {
    const data = new FormData();
    data.append('file', file);
    if (category) data.append('category', category);
    if (savePath) data.append('savePath', savePath);
    if (paused) data.append('paused', 'true');
    return fetchJson<Torrent>(`${BASE_URL}/torrents`, { method: 'POST', body: data });
  },

  // Categories
  getCategories: () => fetchJson<Category[]>(`${BASE_URL}/categories`),
  addCategory: (category: Partial<Category>) =>
    fetchJson<Category>(`${BASE_URL}/categories`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(category),
    }),
  deleteCategory: (id: number) => fetch(`${BASE_URL}/categories/${id}`, { method: 'DELETE' }),

  // System
  getSystemStatus: () => fetchJson<SystemStatus>(`${BASE_URL}/system/status`),
};
