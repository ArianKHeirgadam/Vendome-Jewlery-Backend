import {
  type FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";
import {
  AlertTriangle,
  Check,
  Copy,
  Eye,
  EyeOff,
  KeyRound,
  LoaderCircle,
  Languages,
  LockKeyhole,
  ServerCog,
  ShieldCheck,
  Wifi,
} from "lucide-react";
import { ApiError } from "./authApi";
import { useAuthentication } from "./AuthContext";
import type { LoginStatus, MfaSetupResult } from "./auth.types";
import { useLocale } from "../../i18n/LocaleContext";

type LoginStage = "credentials" | "mfa" | "mfa-enrollment";

function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof TypeError) {
    return "ارتباط با API برقرار نشد. آدرس سرور و اجرای GoldInvoice.Api را بررسی کن.";
  }
  return "درخواست انجام نشد؛ دوباره تلاش کن.";
}

export function AuthenticationSplash() {
  return (
    <main className="auth-page auth-page--splash" aria-busy="true">
      <div className="auth-splash-mark">
        <span>VENDÔME</span>
        <i />
        <LoaderCircle className="spin" size={24} aria-hidden="true" />
        <p>در حال بازیابی نشست امن…</p>
      </div>
    </main>
  );
}

export function LoginPage() {
  const { language, toggleLanguage } = useLocale();
  const {
    runtime,
    login,
    startMfaEnrollment,
    completeMfaEnrollment,
    configureApiBaseUrl,
  } = useAuthentication();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [mfaCode, setMfaCode] = useState("");
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [stage, setStage] = useState<LoginStage>("credentials");
  const [mfaSetup, setMfaSetup] = useState<MfaSetupResult | null>(null);
  const [apiBaseUrl, setApiBaseUrl] = useState(
    runtime?.apiBaseUrl ?? "https://localhost:7156",
  );
  const [busy, setBusy] = useState(false);
  const [connectionBusy, setConnectionBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [connectionMessage, setConnectionMessage] = useState<string | null>(
    null,
  );

  useEffect(() => {
    if (runtime?.apiBaseUrl) setApiBaseUrl(runtime.apiBaseUrl);
  }, [runtime?.apiBaseUrl]);

  const normalizedMfaCode = useMemo(
    () => mfaCode.replace(/[\s-]/g, ""),
    [mfaCode],
  );

  const handleLoginStatus = async (result: LoginStatus) => {
    if (result === "mfa_required") {
      setStage("mfa");
      setMfaCode("");
      setError(null);
      return;
    }

    if (result === "mfa_enrollment_required") {
      const setup = await startMfaEnrollment();
      setMfaSetup(setup);
      setStage("mfa-enrollment");
      setMfaCode("");
    }
  };

  const submitLogin = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await login({
        email,
        password,
        authenticatorCode:
          stage === "mfa" && !useRecoveryCode
            ? normalizedMfaCode
            : undefined,
        recoveryCode:
          stage === "mfa" && useRecoveryCode
            ? normalizedMfaCode
            : undefined,
      });
      await handleLoginStatus(result);
    } catch (requestError) {
      setError(errorMessage(requestError));
    } finally {
      setBusy(false);
    }
  };

  const submitMfaEnrollment = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await completeMfaEnrollment(normalizedMfaCode);
    } catch (requestError) {
      setError(errorMessage(requestError));
    } finally {
      setBusy(false);
    }
  };

  const saveConnection = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setConnectionBusy(true);
    setConnectionMessage(null);
    setError(null);
    try {
      const configurationUrl = apiBaseUrl.trim().replace(/\/+$/, "");
      await configureApiBaseUrl(configurationUrl);
      const response = await fetch(`${configurationUrl}/health/live`, {
        headers: { Accept: "text/plain" },
      });
      if (!response.ok) throw new Error("health-check-failed");
      setConnectionMessage("اتصال با API برقرار شد.");
    } catch (requestError) {
      setError(errorMessage(requestError));
    } finally {
      setConnectionBusy(false);
    }
  };

  return (
    <main className="auth-page">
      <button
        className="auth-language-button"
        type="button"
        aria-label={language === "fa" ? "تغییر به انگلیسی" : "تغییر به فارسی"}
        onClick={toggleLanguage}
      >
        <Languages size={17} />
        <span>{language === "fa" ? "English" : "فارسی"}</span>
      </button>
      <section className="auth-brand-panel" aria-label="مِزون وندوم">
        <div className="auth-brand-content">
          <span className="auth-eyebrow">JEWELRY MANAGEMENT SUITE</span>
          <h1>VENDÔME</h1>
          <span className="auth-brand-rule" />
          <h2>مدیریت دقیق، درخشش ماندگار</h2>
          <p>
            فروش، فاکتور، موجودی و قیمت بازار در یک فضای امن و یکپارچه؛
            مخصوص مجموعهٔ وندوم.
          </p>
          <div className="auth-security-note">
            <ShieldCheck size={19} aria-hidden="true" />
            <span>نشست رمزگذاری‌شده · اتصال مستقیم به API داخلی</span>
          </div>
        </div>
        <div className="auth-orbit auth-orbit--one" />
        <div className="auth-orbit auth-orbit--two" />
      </section>

      <section className="auth-form-panel">
        <div className="auth-mobile-brand">VENDÔME</div>
        <div className="auth-card">
          <div className="auth-card-heading">
            <span className="auth-lock-icon">
              {stage === "mfa-enrollment" ? (
                <ShieldCheck size={22} />
              ) : (
                <LockKeyhole size={21} />
              )}
            </span>
            <div>
              <h2>
                {stage === "credentials" && "ورود به مدیریت"}
                {stage === "mfa" && "تأیید ورود دومرحله‌ای"}
                {stage === "mfa-enrollment" && "فعال‌سازی ورود امن"}
              </h2>
              <p>
                {stage === "credentials" &&
                  "مالک با ایمیل وارد می‌شود؛ مدیر و کارمند با شماره موبایل."}
                {stage === "mfa" &&
                  "کد برنامهٔ Authenticator یا کد بازیابی را وارد کن."}
                {stage === "mfa-enrollment" &&
                  "کلید را در Authenticator ثبت کن و کد شش‌رقمی را بنویس."}
              </p>
            </div>
          </div>

          {stage !== "mfa-enrollment" ? (
            <form className="auth-form" onSubmit={submitLogin}>
              <label>
                <span>ایمیل مالک / موبایل کارکنان</span>
                <input
                  type="text"
                  autoComplete="username"
                  dir="ltr"
                  required
                  maxLength={320}
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  disabled={busy || stage === "mfa"}
                  placeholder="owner@vendome.local / 09120000000"
                />
              </label>
              <label>
                <span>رمز عبور</span>
                <div className="password-field">
                  <input
                    type={passwordVisible ? "text" : "password"}
                    autoComplete="current-password"
                    dir="ltr"
                    required
                    maxLength={256}
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    disabled={busy || stage === "mfa"}
                    placeholder="••••••••••••"
                  />
                  <button
                    type="button"
                    aria-label={passwordVisible ? "پنهان‌کردن رمز" : "نمایش رمز"}
                    onClick={() => setPasswordVisible((visible) => !visible)}
                  >
                    {passwordVisible ? <EyeOff size={18} /> : <Eye size={18} />}
                  </button>
                </div>
              </label>

              {stage === "mfa" && (
                <div className="mfa-fieldset">
                  <label>
                    <span>
                      {useRecoveryCode ? "کد بازیابی" : "کد Authenticator"}
                    </span>
                    <input
                      type="text"
                      inputMode={useRecoveryCode ? "text" : "numeric"}
                      autoComplete="one-time-code"
                      dir="ltr"
                      required
                      minLength={useRecoveryCode ? 8 : 6}
                      maxLength={useRecoveryCode ? 64 : 16}
                      value={mfaCode}
                      onChange={(event) => setMfaCode(event.target.value)}
                      placeholder={useRecoveryCode ? "XXXX-XXXX" : "123 456"}
                      autoFocus
                    />
                  </label>
                  <button
                    className="auth-text-button"
                    type="button"
                    onClick={() => {
                      setUseRecoveryCode((active) => !active);
                      setMfaCode("");
                    }}
                  >
                    {useRecoveryCode
                      ? "استفاده از کد Authenticator"
                      : "استفاده از کد بازیابی"}
                  </button>
                </div>
              )}

              {error && <AuthError message={error} />}

              <button className="auth-primary-button" type="submit" disabled={busy}>
                {busy ? <LoaderCircle className="spin" size={19} /> : <KeyRound size={18} />}
                <span>{stage === "mfa" ? "تأیید و ورود" : "ورود امن"}</span>
              </button>

              {stage === "mfa" && (
                <button
                  className="auth-secondary-button"
                  type="button"
                  onClick={() => {
                    setStage("credentials");
                    setMfaCode("");
                    setError(null);
                  }}
                >
                  بازگشت و اصلاح اطلاعات
                </button>
              )}
            </form>
          ) : (
            <form className="auth-form" onSubmit={submitMfaEnrollment}>
              <div className="mfa-setup-box">
                <span>کلید راه‌اندازی</span>
                <strong dir="ltr">{mfaSetup?.sharedKey ?? "—"}</strong>
                <button
                  type="button"
                  onClick={() =>
                    void navigator.clipboard.writeText(mfaSetup?.sharedKey ?? "")
                  }
                >
                  <Copy size={15} /> کپی کلید
                </button>
              </div>
              <label>
                <span>کد شش‌رقمی Authenticator</span>
                <input
                  type="text"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  dir="ltr"
                  required
                  minLength={6}
                  maxLength={16}
                  value={mfaCode}
                  onChange={(event) => setMfaCode(event.target.value)}
                  placeholder="123 456"
                  autoFocus
                />
              </label>
              {error && <AuthError message={error} />}
              <button className="auth-primary-button" type="submit" disabled={busy}>
                {busy ? <LoaderCircle className="spin" size={19} /> : <ShieldCheck size={18} />}
                <span>فعال‌سازی و ورود</span>
              </button>
            </form>
          )}

          {stage === "credentials" && <details className="connection-settings">
            <summary>
              <ServerCog size={17} /> تنظیم اتصال به سرور
            </summary>
            <form onSubmit={saveConnection}>
              <label>
                <span>آدرس GoldInvoice.Api</span>
                <input
                  type="url"
                  dir="ltr"
                  required
                  value={apiBaseUrl}
                  onChange={(event) => setApiBaseUrl(event.target.value)}
                  placeholder="https://localhost:7156"
                />
              </label>
              {runtime?.isInsecureTransport && (
                <p className="transport-warning">
                  <AlertTriangle size={15} /> اتصال HTTP فقط برای توسعه یا شبکهٔ
                  داخلی موقت مناسب است.
                </p>
              )}
              {connectionMessage && (
                <p className="connection-success">
                  <Wifi size={15} /> {connectionMessage}
                </p>
              )}
              <button type="submit" disabled={connectionBusy}>
                {connectionBusy ? <LoaderCircle className="spin" size={16} /> : <Wifi size={16} />}
                ذخیره و آزمایش اتصال
              </button>
            </form>
          </details>}
        </div>
        <p className="auth-version">Vendome Suite · Phase 7A · Secure Client</p>
      </section>
    </main>
  );
}

function AuthError({ message }: { message: string }) {
  return (
    <p className="auth-error" role="alert">
      <AlertTriangle size={17} aria-hidden="true" />
      <span>{message}</span>
    </p>
  );
}

export function RecoveryCodesPage() {
  const { recoveryCodes, acknowledgeRecoveryCodes } = useAuthentication();
  const [copied, setCopied] = useState(false);
  if (!recoveryCodes) return null;

  const copyCodes = async () => {
    await navigator.clipboard.writeText(recoveryCodes.join("\n"));
    setCopied(true);
  };

  return (
    <main className="auth-page auth-page--recovery">
      <section className="recovery-card">
        <span className="auth-lock-icon"><ShieldCheck size={24} /></span>
        <h1>کدهای بازیابی را همین حالا ذخیره کن</h1>
        <p>
          هر کد فقط یک‌بار قابل استفاده است. بعد از بستن این صفحه دوباره نمایش
          داده نمی‌شوند.
        </p>
        <div className="recovery-code-grid" dir="ltr">
          {recoveryCodes.map((code) => <code key={code}>{code}</code>)}
        </div>
        <button className="auth-secondary-button" type="button" onClick={copyCodes}>
          {copied ? <Check size={18} /> : <Copy size={18} />}
          {copied ? "کپی شد" : "کپی همهٔ کدها"}
        </button>
        <button className="auth-primary-button" type="button" onClick={acknowledgeRecoveryCodes}>
          <ShieldCheck size={18} /> کدها را امن ذخیره کردم
        </button>
      </section>
    </main>
  );
}
