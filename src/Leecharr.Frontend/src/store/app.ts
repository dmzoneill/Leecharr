import { create } from "zustand";
import type { CurrentUser } from "../api/types";

interface AppState {
  apiKey: string;
  isConnected: boolean;
  currentUser: CurrentUser | null;
  setApiKey: (key: string) => void;
  setConnected: (connected: boolean) => void;
  setCurrentUser: (user: CurrentUser | null) => void;
}

export const useAppStore = create<AppState>((set) => ({
  apiKey: "",
  isConnected: false,
  currentUser: null,
  setApiKey: (key: string) => set({ apiKey: key }),
  setConnected: (connected: boolean) => set({ isConnected: connected }),
  setCurrentUser: (user: CurrentUser | null) => set({ currentUser: user }),
}));
