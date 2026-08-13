export type AuthenticationStatus =
  | "booting"
  | "anonymous"
  | "authenticated";

export type LoginStatus =
  | "authenticated"
  | "mfa_required"
  | "mfa_enrollment_required";

export interface RuntimeConfiguration {
  apiBaseUrl: string;
  isDesktop: boolean;
  hasRefreshToken: boolean;
  isInsecureTransport: boolean;
}

export interface LoginRequest {
  email: string;
  password: string;
  authenticatorCode?: string;
  recoveryCode?: string;
}

export interface ClientTokens {
  tokenType: "Bearer";
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
  sessionId: string;
}

export interface LoginResult {
  status: LoginStatus;
  tokens?: ClientTokens;
  mfaEnrollmentToken?: string;
}

export interface MfaSetupResult {
  sharedKey: string;
  authenticatorUri: string;
  enrollmentToken: string;
}

export interface MfaEnableResult {
  tokens: ClientTokens;
  recoveryCodes: string[];
}

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  emailConfirmed: boolean;
  mfaEnabled: boolean;
  roles: string[];
  permissions: string[];
  sessionId: string;
}

export interface AuthenticationContextValue {
  status: AuthenticationStatus;
  runtime: RuntimeConfiguration | null;
  user: CurrentUser | null;
  accessToken: string | null;
  recoveryCodes: string[] | null;
  login(request: LoginRequest): Promise<LoginStatus>;
  startMfaEnrollment(): Promise<MfaSetupResult>;
  completeMfaEnrollment(code: string): Promise<void>;
  acknowledgeRecoveryCodes(): void;
  logout(): Promise<void>;
  configureApiBaseUrl(apiBaseUrl: string): Promise<void>;
  authorizedFetch(path: string, init?: RequestInit): Promise<Response>;
}
