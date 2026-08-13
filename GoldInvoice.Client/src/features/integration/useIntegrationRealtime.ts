import { useEffect, useRef, useState } from "react";
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import type { AuthenticationContextValue } from "../auth/auth.types";

export type RealtimeStatus =
  | "connecting"
  | "connected"
  | "reconnecting"
  | "offline";

export interface IntegrationEvent {
  eventId: string;
  eventType: string;
  occurredAt: string;
  aggregateType: string;
  aggregateId: string;
  data: unknown;
}

interface IntegrationEventPage {
  items: IntegrationEvent[];
  nextOccurredAt?: string;
  nextEventId?: string;
}

interface RecoveryCursor {
  occurredAt: string;
  eventId: string;
}

interface RealtimeOptions {
  auth: AuthenticationContextValue;
  onEvent: (event: IntegrationEvent) => void;
}

export function useIntegrationRealtime({ auth, onEvent }: RealtimeOptions) {
  const [status, setStatus] = useState<RealtimeStatus>("connecting");
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;

  useEffect(() => {
    if (
      auth.status !== "authenticated" ||
      !auth.runtime ||
      !auth.accessToken ||
      !auth.user
    ) {
      setStatus("offline");
      return;
    }

    let stopped = false;
    const cursorStorageKey = `vendome-integration-cursor:${auth.user.id}`;
    const seen = new Set<string>();

    const readCursor = (): RecoveryCursor | null => {
      const stored = window.localStorage.getItem(cursorStorageKey);
      if (!stored) return null;
      try {
        const cursor = JSON.parse(stored) as RecoveryCursor;
        return cursor.occurredAt && cursor.eventId ? cursor : null;
      } catch {
        return null;
      }
    };

    const remember = (event: IntegrationEvent) => {
      if (seen.has(event.eventId)) return;
      seen.add(event.eventId);
      if (seen.size > 500) {
        const first = seen.values().next().value as string | undefined;
        if (first) seen.delete(first);
      }
      window.localStorage.setItem(
        cursorStorageKey,
        JSON.stringify({ occurredAt: event.occurredAt, eventId: event.eventId }),
      );
      onEventRef.current(event);
    };

    const recover = async () => {
      let cursor = readCursor();
      if (!cursor) return;

      for (let pageNumber = 0; pageNumber < 10 && !stopped; pageNumber += 1) {
        const query = new URLSearchParams({
          afterOccurredAt: cursor.occurredAt,
          afterEventId: cursor.eventId,
          pageSize: "50",
        });
        const response = await auth.authorizedFetch(
          `/api/v1/integration/events?${query.toString()}`,
        );
        if (!response.ok) return;
        const page = (await response.json()) as IntegrationEventPage;
        page.items.forEach(remember);
        if (!page.nextOccurredAt || !page.nextEventId) return;

        const next = {
          occurredAt: page.nextOccurredAt,
          eventId: page.nextEventId,
        };
        if (
          next.occurredAt === cursor.occurredAt &&
          next.eventId === cursor.eventId
        ) return;
        cursor = next;
        window.localStorage.setItem(cursorStorageKey, JSON.stringify(cursor));
      }
    };

    const connection = new HubConnectionBuilder()
      .withUrl(`${auth.runtime.apiBaseUrl}/hubs/integration`, {
        accessTokenFactory: () => auth.accessToken ?? "",
      })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("integrationEvent", remember);
    connection.onreconnecting(() => {
      if (!stopped) setStatus("reconnecting");
    });
    connection.onreconnected(() => {
      if (stopped) return;
      setStatus("connected");
      void recover();
    });
    connection.onclose(() => {
      if (!stopped) setStatus("offline");
    });

    const start = async () => {
      setStatus("connecting");
      try {
        await connection.start();
        if (stopped) return;
        setStatus("connected");
        await recover();
      } catch {
        if (!stopped) setStatus("offline");
      }
    };

    void start();
    return () => {
      stopped = true;
      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop();
      }
    };
  }, [auth.accessToken, auth.runtime, auth.status, auth.user, auth.authorizedFetch]);

  return status;
}
