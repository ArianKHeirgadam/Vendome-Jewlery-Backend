export interface DesktopBridgeFailure {
  code: string;
  message: string;
  status?: number;
}

interface DesktopBridgeResponse<T = unknown> {
  id: string;
  ok: boolean;
  result?: T;
  error?: DesktopBridgeFailure;
}

interface DesktopWebView {
  postMessage(message: unknown): void;
  addEventListener(
    type: "message",
    listener: (event: MessageEvent<DesktopBridgeResponse>) => void,
  ): void;
}

interface DesktopWindow extends Window {
  chrome?: {
    webview?: DesktopWebView;
  };
}

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject: (reason: DesktopBridgeError) => void;
  timeout: number;
}

const pendingRequests = new Map<string, PendingRequest>();
let listenerAttached = false;

function getWebView(): DesktopWebView | undefined {
  return (window as DesktopWindow).chrome?.webview;
}

function attachListener(webView: DesktopWebView) {
  if (listenerAttached) return;

  webView.addEventListener("message", (event) => {
    const response = event.data;
    if (!response || typeof response.id !== "string") return;

    const pending = pendingRequests.get(response.id);
    if (!pending) return;

    window.clearTimeout(pending.timeout);
    pendingRequests.delete(response.id);

    if (response.ok) {
      pending.resolve(response.result);
      return;
    }

    pending.reject(
      new DesktopBridgeError(
        response.error?.message ?? "برنامهٔ دسکتاپ نتوانست درخواست را انجام دهد.",
        response.error?.code ?? "desktop_bridge_error",
        response.error?.status,
      ),
    );
  });
  listenerAttached = true;
}

export class DesktopBridgeError extends Error {
  constructor(
    message: string,
    public readonly code: string,
    public readonly status?: number,
  ) {
    super(message);
    this.name = "DesktopBridgeError";
  }
}

export function isDesktopHost(): boolean {
  return Boolean(getWebView());
}

export function sendDesktopCommand<T>(
  type: string,
  payload: unknown = {},
  timeoutMilliseconds = 30_000,
): Promise<T> {
  const webView = getWebView();
  if (!webView) {
    return Promise.reject(
      new DesktopBridgeError(
        "این قابلیت فقط داخل برنامهٔ دسکتاپ در دسترس است.",
        "desktop_host_unavailable",
      ),
    );
  }

  attachListener(webView);
  const id = crypto.randomUUID();

  return new Promise<T>((resolve, reject) => {
    const timeout = window.setTimeout(() => {
      pendingRequests.delete(id);
      reject(
        new DesktopBridgeError(
          "پاسخ برنامهٔ دسکتاپ بیش از حد طول کشید.",
          "desktop_bridge_timeout",
        ),
      );
    }, timeoutMilliseconds);

    pendingRequests.set(id, {
      resolve: (value) => resolve(value as T),
      reject,
      timeout,
    });

    webView.postMessage({ id, type, payload });
  });
}
