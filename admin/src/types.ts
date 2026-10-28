// 与 backend/store.go 的 JSON 字段保持一致。
export const priorities = ["low", "medium", "high"] as const;
export const statuses = ["open", "in_progress", "resolved"] as const;
export type Priority = (typeof priorities)[number];
export type IssueStatus = (typeof statuses)[number];

export interface Issue {
  id: string;
  title: string;
  description: string;
  priority: Priority;
  status: IssueStatus;
  position: { x: number; y: number; z: number };
  createdAt: string;
  updatedAt: string;
}

export const priorityLabels: Record<Priority, string> = {
  low: "低",
  medium: "中",
  high: "高",
};
export const statusLabels: Record<IssueStatus, string> = {
  open: "待处理",
  in_progress: "处理中",
  resolved: "已解决",
};
