import {
  DesktopBridgeError,
  isDesktopHost,
  sendDesktopCommand,
} from "../../platform/desktopBridge";
import type {
  ClientTokens,
  CurrentUser,
  LoginRequest,
  LoginResult,
  MfaEnableResult,
  MfaSetupResult,
  RuntimeConfiguration,
} from "./auth.types";

const apiBaseStorageKey = "vendome-api-base-url";
const refreshTokenStorageKey = "vendome-refresh-token";
const defaultApiBaseUrl =
  import.meta.env.VITE_API_BASE_URL?.trim() || "https://localhost:7156";

interface RawTokenResponse extends ClientTokens {
  refreshToken: string;
}

interface RawLoginResult extends Omit<LoginResult, "tokens"> {
  tokens?: RawTokenResponse;
}

interface RawMfaEnableResult extends Omit<MfaEnableResult, "tokens"> {
  tokens: RawTokenResponse;
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status?: number,
    public readonly code = "api_error",
  ) {
    super(message);
    this.name = "ApiError";
  }
}

function normalizeApiBaseUrl(value: string): string {
  const normalized = value.trim().replace(/\/+$/, "");
  let url: URL;
  try {
    url = new URL(normalized);
  } catch {
    throw new ApiError("آدرس API معتبر نیست.", 400, "invalid_api_url");
  }

  if (
    !["http:", "https:"].includes(url.protocol) ||
    url.username ||
    url.password ||
    url.search ||
    url.hash ||
    (url.pathname !== "/" && url.pathname !== "")
  ) {
    throw new ApiError(
      "آدرس API باید فقط شامل پروتکل، نام سرور و پورت باشد.",
      400,
      "invalid_api_url",
    );
  }

  return normalized;
}

function isInsecureTransport(apiBaseUrl: string): boolean {
  return new URL(apiBaseUrl).protocol === "http:";
}

function bridgeError(error: unknown): never {
  if (error instanceof DesktopBridgeError) {
    throw new ApiError(error.message, error.status, error.code);
  }
  throw error;
}

async function parseResponse<T>(response: Response): Promise<T> {
  const bodyText = await response.text();
  let body: T | ProblemDetails | undefined;
  if (bodyText) {
    try {
      body = JSON.parse(bodyText) as T | ProblemDetails;
    } catch {
      body = undefined;
    }
  }

  if (!response.ok) {
    const problem = body as ProblemDetails | undefined;
    const firstValidationError = problem?.errors
      ? Object.values(problem.errors).flat()[0]
      : undefined;
    throw new ApiError(
      firstValidationError ||
        problem?.detail ||
        problem?.title ||
        "سرور نتوانست درخواست را انجام دهد.",
      response.status,
    );
  }

  return (body ?? ({} as T)) as T;
}

async function apiRequest<T>(
  runtime: RuntimeConfiguration,
  path: string,
  init: RequestInit,
): Promise<T> {
  const response = await fetch(`${runtime.apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      ...(init.body ? { "Content-Type": "application/json" } : {}),
      ...init.headers,
    },
  });
  return parseResponse<T>(response);
}

function persistBrowserTokens(tokens: RawTokenResponse): ClientTokens {
  window.sessionStorage.setItem(refreshTokenStorageKey, tokens.refreshToken);
  const { refreshToken: _, ...clientTokens } = tokens;
  return clientTokens;
}

async function desktopCommand<T>(type: string, payload?: unknown): Promise<T> {
  try {
    return await sendDesktopCommand<T>(type, payload);
  } catch (error) {
    return bridgeError(error);
  }
}

export async function getRuntimeConfiguration(): Promise<RuntimeConfiguration> {
  if (isDesktopHost()) {
    return desktopCommand<RuntimeConfiguration>("runtime.get");
  }

  const apiBaseUrl = normalizeApiBaseUrl(
    window.sessionStorage.getItem(apiBaseStorageKey) || defaultApiBaseUrl,
  );
  return {
    apiBaseUrl,
    isDesktop: false,
    hasRefreshToken: Boolean(
      window.sessionStorage.getItem(refreshTokenStorageKey),
    ),
    isInsecureTransport: isInsecureTransport(apiBaseUrl),
  };
}

export async function configureRuntime(
  apiBaseUrl: string,
): Promise<RuntimeConfiguration> {
  const normalized = normalizeApiBaseUrl(apiBaseUrl);
  if (isDesktopHost()) {
    return desktopCommand<RuntimeConfiguration>("runtime.configure", {
      apiBaseUrl: normalized,
    });
  }

  const previous = normalizeApiBaseUrl(
    window.sessionStorage.getItem(apiBaseStorageKey) || defaultApiBaseUrl,
  );
  window.sessionStorage.setItem(apiBaseStorageKey, normalized);
  if (previous && previous !== normalized) {
    window.sessionStorage.removeItem(refreshTokenStorageKey);
  }
  return {
    apiBaseUrl: normalized,
    isDesktop: false,
    hasRefreshToken: Boolean(
      window.sessionStorage.getItem(refreshTokenStorageKey),
    ),
    isInsecureTransport: isInsecureTransport(normalized),
  };
}

export async function login(
  runtime: RuntimeConfiguration,
  request: LoginRequest,
): Promise<LoginResult> {
  if (runtime.isDesktop) {
    return desktopCommand<LoginResult>("auth.login", request);
  }

  const result = await apiRequest<RawLoginResult>(
    runtime,
    "/api/v1/auth/login",
    { method: "POST", body: JSON.stringify(request) },
  );
  return {
    ...result,
    tokens: result.tokens ? persistBrowserTokens(result.tokens) : undefined,
  };
}

let refreshInFlight: Promise<ClientTokens> | null = null;

export function refreshAccessToken(
  runtime: RuntimeConfiguration,
): Promise<ClientTokens> {
  if (refreshInFlight) return refreshInFlight;

  refreshInFlight = (async () => {
    if (runtime.isDesktop) {
      return desktopCommand<ClientTokens>("auth.refresh");
    }

    const refreshToken = window.sessionStorage.getItem(refreshTokenStorageKey);
    if (!refreshToken) {
      throw new ApiError(
        "نشست ذخیره‌شده‌ای وجود ندارد.",
        401,
        "session_unavailable",
      );
    }
    const tokens = await apiRequest<RawTokenResponse>(
      runtime,
      "/api/v1/auth/refresh",
      { method: "POST", body: JSON.stringify({ refreshToken }) },
    );
    return persistBrowserTokens(tokens);
  })().finally(() => {
    refreshInFlight = null;
  });

  return refreshInFlight;
}

export async function startMfaEnrollment(
  runtime: RuntimeConfiguration,
  enrollmentToken: string,
): Promise<MfaSetupResult> {
  if (runtime.isDesktop) {
    return desktopCommand<MfaSetupResult>("auth.mfa.setup", {
      enrollmentToken,
    });
  }

  return apiRequest<MfaSetupResult>(runtime, "/api/v1/auth/mfa/setup", {
    method: "POST",
    body: JSON.stringify({ enrollmentToken }),
  });
}

export async function completeMfaEnrollment(
  runtime: RuntimeConfiguration,
  enrollmentToken: string,
  authenticatorCode: string,
): Promise<MfaEnableResult> {
  if (runtime.isDesktop) {
    return desktopCommand<MfaEnableResult>("auth.mfa.enable", {
      enrollmentToken,
      authenticatorCode,
    });
  }

  const result = await apiRequest<RawMfaEnableResult>(
    runtime,
    "/api/v1/auth/mfa/enable",
    {
      method: "POST",
      body: JSON.stringify({ enrollmentToken, authenticatorCode }),
    },
  );
  return { ...result, tokens: persistBrowserTokens(result.tokens) };
}

export async function getCurrentUser(
  runtime: RuntimeConfiguration,
  accessToken: string,
): Promise<CurrentUser> {
  return apiRequest<CurrentUser>(runtime, "/api/v1/auth/me", {
    method: "GET",
    headers: { Authorization: `Bearer ${accessToken}` },
  });
}

export async function logout(
  runtime: RuntimeConfiguration,
  accessToken: string | null,
): Promise<void> {
  if (runtime.isDesktop) {
    try {
      await desktopCommand("auth.logout", { accessToken });
    } catch {
      await desktopCommand("auth.clear").catch(() => undefined);
    }
    return;
  }

  try {
    if (accessToken) {
      await apiRequest(runtime, "/api/v1/auth/logout", {
        method: "POST",
        headers: { Authorization: `Bearer ${accessToken}` },
      });
    }
  } catch {
    // Local sign-out must still complete when the API is temporarily offline.
  } finally {
    window.sessionStorage.removeItem(refreshTokenStorageKey);
  }
}

export async function clearLocalSession(
  runtime: RuntimeConfiguration,
): Promise<void> {
  if (runtime.isDesktop) {
    await desktopCommand("auth.clear");
  } else {
    window.sessionStorage.removeItem(refreshTokenStorageKey);
  }
}
