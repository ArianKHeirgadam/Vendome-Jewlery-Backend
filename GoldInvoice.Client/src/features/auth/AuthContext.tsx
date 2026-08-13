import {
  createContext,
  type PropsWithChildren,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import {
  ApiError,
  clearLocalSession,
  completeMfaEnrollment as completeMfaEnrollmentRequest,
  configureRuntime,
  getCurrentUser,
  getRuntimeConfiguration,
  login as loginRequest,
  logout as logoutRequest,
  refreshAccessToken,
  startMfaEnrollment as startMfaEnrollmentRequest,
} from "./authApi";
import type {
  AuthenticationContextValue,
  AuthenticationStatus,
  ClientTokens,
  CurrentUser,
  LoginRequest,
  LoginStatus,
  MfaSetupResult,
  RuntimeConfiguration,
} from "./auth.types";

const AuthenticationContext = createContext<AuthenticationContextValue | null>(
  null,
);

function isAuthenticationRejection(error: unknown): boolean {
  return error instanceof ApiError &&
    (error.status === 401 || error.status === 403 || error.code === "session_unavailable");
}

export function AuthenticationProvider({ children }: PropsWithChildren) {
  const [status, setStatus] = useState<AuthenticationStatus>("booting");
  const [runtime, setRuntime] = useState<RuntimeConfiguration | null>(null);
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [accessTokenExpiresAt, setAccessTokenExpiresAt] = useState<string | null>(
    null,
  );
  const [mfaEnrollmentToken, setMfaEnrollmentToken] = useState<string | null>(
    null,
  );
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);

  const runtimeRef = useRef<RuntimeConfiguration | null>(null);
  const accessTokenRef = useRef<string | null>(null);
  const mfaEnrollmentTokenRef = useRef<string | null>(null);
  const mountedRef = useRef(true);

  const clearAuthenticationState = useCallback(() => {
    accessTokenRef.current = null;
    setAccessToken(null);
    setAccessTokenExpiresAt(null);
    setUser(null);
    mfaEnrollmentTokenRef.current = null;
    setMfaEnrollmentToken(null);
    setRecoveryCodes(null);
    setStatus("anonymous");
  }, []);

  const acceptTokens = useCallback(
    async (activeRuntime: RuntimeConfiguration, tokens: ClientTokens) => {
      accessTokenRef.current = tokens.accessToken;
      setAccessToken(tokens.accessToken);
      setAccessTokenExpiresAt(tokens.accessTokenExpiresAt);
      const currentUser = await getCurrentUser(
        activeRuntime,
        tokens.accessToken,
      );
      if (!mountedRef.current) return;
      setUser(currentUser);
      setStatus("authenticated");
    },
    [],
  );

  const refreshSession = useCallback(async (): Promise<string> => {
    const activeRuntime = runtimeRef.current;
    if (!activeRuntime) {
      throw new ApiError("تنظیمات اتصال هنوز آماده نیست.");
    }

    try {
      const tokens = await refreshAccessToken(activeRuntime);
      await acceptTokens(activeRuntime, tokens);
      return tokens.accessToken;
    } catch (error) {
      if (isAuthenticationRejection(error)) {
        await clearLocalSession(activeRuntime).catch(() => undefined);
        clearAuthenticationState();
      }
      throw error;
    }
  }, [acceptTokens, clearAuthenticationState]);

  useEffect(() => {
    mountedRef.current = true;
    let cancelled = false;

    const bootstrap = async () => {
      try {
        const configuration = await getRuntimeConfiguration();
        if (cancelled) return;
        runtimeRef.current = configuration;
        setRuntime(configuration);

        if (!configuration.hasRefreshToken) {
          setStatus("anonymous");
          return;
        }

        try {
          const tokens = await refreshAccessToken(configuration);
          if (!cancelled) await acceptTokens(configuration, tokens);
        } catch (error) {
          if (isAuthenticationRejection(error)) {
            await clearLocalSession(configuration).catch(() => undefined);
          }
          if (!cancelled) clearAuthenticationState();
        }
      } catch {
        if (!cancelled) setStatus("anonymous");
      }
    };

    void bootstrap();
    return () => {
      cancelled = true;
      mountedRef.current = false;
    };
  }, [acceptTokens, clearAuthenticationState]);

  useEffect(() => {
    if (status !== "authenticated" || !accessTokenExpiresAt) return;

    const expiresAt = Date.parse(accessTokenExpiresAt);
    const refreshAt = Math.max(5_000, expiresAt - Date.now() - 60_000);
    const timeout = window.setTimeout(() => {
      void refreshSession().catch(() => undefined);
    }, refreshAt);
    return () => window.clearTimeout(timeout);
  }, [accessTokenExpiresAt, refreshSession, status]);

  const login = useCallback(
    async (request: LoginRequest): Promise<LoginStatus> => {
      const activeRuntime = runtimeRef.current;
      if (!activeRuntime) {
        throw new ApiError("ابتدا آدرس API را ذخیره کن.");
      }

      const result = await loginRequest(activeRuntime, request);
      if (result.status === "authenticated" && result.tokens) {
        await acceptTokens(activeRuntime, result.tokens);
      } else if (
        result.status === "mfa_enrollment_required" &&
        result.mfaEnrollmentToken
      ) {
        mfaEnrollmentTokenRef.current = result.mfaEnrollmentToken;
        setMfaEnrollmentToken(result.mfaEnrollmentToken);
      }
      return result.status;
    },
    [acceptTokens],
  );

  const startMfaEnrollment = useCallback(async (): Promise<MfaSetupResult> => {
    const activeRuntime = runtimeRef.current;
    const enrollmentToken = mfaEnrollmentTokenRef.current;
    if (!activeRuntime || !enrollmentToken) {
      throw new ApiError("توکن راه‌اندازی ورود دومرحله‌ای موجود نیست.");
    }

    const setup = await startMfaEnrollmentRequest(
      activeRuntime,
      enrollmentToken,
    );
    mfaEnrollmentTokenRef.current = setup.enrollmentToken;
    setMfaEnrollmentToken(setup.enrollmentToken);
    return setup;
  }, []);

  const completeMfaEnrollment = useCallback(
    async (code: string): Promise<void> => {
      const activeRuntime = runtimeRef.current;
      const enrollmentToken = mfaEnrollmentTokenRef.current;
      if (!activeRuntime || !enrollmentToken) {
        throw new ApiError("فرایند ورود دومرحله‌ای منقضی شده است.");
      }

      const result = await completeMfaEnrollmentRequest(
        activeRuntime,
        enrollmentToken,
        code,
      );
      setRecoveryCodes(result.recoveryCodes);
      mfaEnrollmentTokenRef.current = null;
      setMfaEnrollmentToken(null);
      await acceptTokens(activeRuntime, result.tokens);
    },
    [acceptTokens],
  );

  const logout = useCallback(async () => {
    const activeRuntime = runtimeRef.current;
    if (!activeRuntime) {
      clearAuthenticationState();
      return;
    }

    try {
      await logoutRequest(activeRuntime, accessTokenRef.current);
    } finally {
      clearAuthenticationState();
    }
  }, [clearAuthenticationState]);

  const configureApiBaseUrl = useCallback(
    async (apiBaseUrl: string) => {
      const configuration = await configureRuntime(apiBaseUrl);
      runtimeRef.current = configuration;
      setRuntime(configuration);
      clearAuthenticationState();
    },
    [clearAuthenticationState],
  );

  const authorizedFetch = useCallback(
    async (path: string, init: RequestInit = {}): Promise<Response> => {
      const activeRuntime = runtimeRef.current;
      if (!activeRuntime) {
        throw new ApiError("تنظیمات اتصال هنوز آماده نیست.");
      }

      let token = accessTokenRef.current;
      if (!token) token = await refreshSession();

      const send = (activeToken: string) =>
        fetch(`${activeRuntime.apiBaseUrl}${path}`, {
          ...init,
          headers: {
            Accept: "application/json",
            ...init.headers,
            Authorization: `Bearer ${activeToken}`,
          },
        });

      let response = await send(token);
      if (response.status === 401) {
        token = await refreshSession();
        response = await send(token);
      }
      return response;
    },
    [refreshSession],
  );

  const value: AuthenticationContextValue = {
    status,
    runtime,
    user,
    accessToken,
    recoveryCodes,
    login,
    startMfaEnrollment,
    completeMfaEnrollment,
    acknowledgeRecoveryCodes: () => setRecoveryCodes(null),
    logout,
    configureApiBaseUrl,
    authorizedFetch,
  };

  return (
    <AuthenticationContext.Provider value={value}>
      {children}
    </AuthenticationContext.Provider>
  );
}

export function useAuthentication(): AuthenticationContextValue {
  const context = useContext(AuthenticationContext);
  if (!context) {
    throw new Error(
      "useAuthentication must be used inside AuthenticationProvider.",
    );
  }
  return context;
}
