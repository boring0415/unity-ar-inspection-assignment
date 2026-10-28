import { priorities, statuses, type Issue, type IssueStatus } from "./types";

export const apiBaseURL = (
  import.meta.env.VITE_API_BASE_URL?.trim() || "/api"
).replace(/\/+$/, "");

function isIssue(value: unknown): value is Issue {
  if (!value || typeof value !== "object") return false;
  const item = value as Issue;
  return (
    typeof item.id === "string" &&
    typeof item.title === "string" &&
    typeof item.description === "string" &&
    priorities.includes(item.priority) &&
    statuses.includes(item.status) &&
    !!item.position &&
    [item.position.x, item.position.y, item.position.z].every(
      Number.isFinite,
    ) &&
    typeof item.createdAt === "string" &&
    Number.isFinite(Date.parse(item.createdAt)) &&
    typeof item.updatedAt === "string" &&
    Number.isFinite(Date.parse(item.updatedAt))
  );
}

async function request(path: string, init: RequestInit = {}): Promise<unknown> {
  const timeout = new AbortController();
  const timer = window.setTimeout(() => timeout.abort(), 10000);
  const signal = init.signal
    ? AbortSignal.any([init.signal, timeout.signal])
    : timeout.signal;
  try {
    const response = await fetch(`${apiBaseURL}${path}`, { ...init, signal });
    const text = await response.text();
    let payload: unknown;
    try {
      payload = JSON.parse(text);
    } catch {
      if (!response.ok) {
        throw new Error(
          `请求失败（HTTP ${response.status}）。请确认 Go 后端已启动，并检查 API 地址或 Vite 代理配置。`,
        );
      }
      throw new Error("接口未返回有效 JSON，请检查 API 地址是否指向 Go 后端。");
    }
    if (!response.ok) {
      const error = (payload as { error?: { message?: unknown } } | null)
        ?.error;
      const detail =
        typeof error?.message === "string" ? `：${error.message}` : "";
      throw new Error(`请求失败（HTTP ${response.status}）${detail}`);
    }
    return payload;
  } catch (error) {
    if (init.signal?.aborted) throw error;
    if (timeout.signal.aborted) {
      throw new Error("请求超过 10 秒，请确认 Go 后端已启动或网络连接正常。");
    }
    if (error instanceof TypeError) {
      throw new Error(
        "无法连接服务，请确认 Go 后端已启动，并检查 API 地址或 Vite 代理配置。",
      );
    }
    throw error;
  } finally {
    window.clearTimeout(timer);
  }
}

export async function getIssues(signal?: AbortSignal): Promise<Issue[]> {
  const payload = await request("/issues", { signal });
  if (!Array.isArray(payload) || !payload.every(isIssue)) {
    throw new Error("问题列表格式不符合接口约定，请检查后端响应。");
  }
  return payload;
}

export async function updateIssueStatus(
  id: string,
  status: IssueStatus,
): Promise<Issue> {
  const payload = await request(`/issues/${encodeURIComponent(id)}/status`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ status }),
  });
  if (!isIssue(payload) || payload.id !== id || payload.status !== status) {
    throw new Error("更新响应不符合接口约定，请刷新列表确认后端状态。");
  }
  return payload;
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "发生未知错误，请重试。";
}
