import type { AuthenticationContextValue } from "../auth/auth.types";

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

export class OperationalApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
    this.name = "OperationalApiError";
  }
}

export type AuthorizedFetch = AuthenticationContextValue["authorizedFetch"];

export async function apiRequest<T>(
  authorizedFetch: AuthorizedFetch,
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const response = await authorizedFetch(path, {
    ...init,
    headers: {
      ...(init.body && !(init.body instanceof FormData)
        ? { "Content-Type": "application/json" }
        : {}),
      ...init.headers,
    },
  });
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
    const validationMessage = problem?.errors
      ? Object.values(problem.errors).flat()[0]
      : undefined;
    throw new OperationalApiError(
      validationMessage ||
        problem?.detail ||
        problem?.title ||
        "سرور نتوانست این عملیات را انجام دهد.",
      response.status,
    );
  }

  return (body ?? ({} as T)) as T;
}

export async function optionalApiRequest<T>(
  authorizedFetch: AuthorizedFetch,
  path: string,
): Promise<T | null> {
  try {
    return await apiRequest<T>(authorizedFetch, path);
  } catch (error) {
    if (error instanceof OperationalApiError && error.status === 404) return null;
    throw error;
  }
}

export function createIdempotencyKey(prefix: string): string {
  return `${prefix}-${crypto.randomUUID()}`;
}
