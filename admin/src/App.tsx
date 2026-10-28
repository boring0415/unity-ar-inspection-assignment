import { useCallback, useEffect, useState } from "react";
import { apiBaseURL, errorMessage, getIssues, updateIssueStatus } from "./api";
import {
  priorityLabels,
  statusLabels,
  statuses,
  type Issue,
  type IssueStatus,
} from "./types";

const dateFormat = new Intl.DateTimeFormat("zh-CN", {
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hour12: false,
});
const formatTime = (value: string) => dateFormat.format(new Date(value));

function IssueRow({
  issue,
  busy,
  saving,
  onSave,
}: {
  issue: Issue;
  busy: boolean;
  saving: boolean;
  onSave: (id: string, status: IssueStatus) => Promise<void>;
}) {
  const [selected, setSelected] = useState<IssueStatus>(issue.status);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    setSelected(issue.status);
    setError("");
  }, [issue.status, issue.updatedAt]);

  async function save() {
    setError("");
    setSaved(false);
    try {
      await onSave(issue.id, selected);
      setSaved(true);
    } catch (failure) {
      setError(
        `状态更新失败：${errorMessage(failure)} 当前界面未确认本次修改，请刷新核对服务端结果。`,
      );
    }
  }

  return (
    <tr>
      <td className="issue-cell">
        <h3>{issue.title}</h3>
        <p className="description">{issue.description || "暂无描述"}</p>
        <p className="issue-id">
          <span>ID</span> <code>{issue.id}</code>
        </p>
      </td>
      <td>
        <span className={`badge priority-${issue.priority}`}>
          <i />
          {priorityLabels[issue.priority]} · {issue.priority}
        </span>
      </td>
      <td>
        <span className={`badge status-${issue.status}`}>
          {statusLabels[issue.status]}
        </span>
        <small className="enum-value">{issue.status}</small>
      </td>
      <td>
        <dl className="coordinates">
          {(["x", "y", "z"] as const).map((axis) => (
            <div key={axis}>
              <dt>{axis.toUpperCase()}</dt>
              <dd>{issue.position[axis]}</dd>
            </div>
          ))}
        </dl>
      </td>
      <td className="time-cell">
        <span>创建</span>
        <time dateTime={issue.createdAt} title={issue.createdAt}>
          {formatTime(issue.createdAt)}
        </time>
        <span>更新</span>
        <time dateTime={issue.updatedAt} title={issue.updatedAt}>
          {formatTime(issue.updatedAt)}
        </time>
      </td>
      <td className="action-cell">
        <label className="sr-only" htmlFor={`status-${issue.id}`}>
          设置「{issue.title}」的处理状态
        </label>
        <select
          id={`status-${issue.id}`}
          value={selected}
          disabled={busy}
          onChange={(event) => {
            setSelected(event.target.value as IssueStatus);
            setError("");
            setSaved(false);
          }}
        >
          {statuses.map((status) => (
            <option key={status} value={status}>
              {statusLabels[status]}
            </option>
          ))}
        </select>
        <button
          className="save-button"
          disabled={busy || selected === issue.status}
          onClick={() => void save()}
        >
          {saving ? "保存中…" : "保存状态"}
        </button>
        {error && (
          <p className="row-error" role="alert">
            {error}
          </p>
        )}
        {saved && (
          <p className="row-success" role="status">
            状态已保存
          </p>
        )}
      </td>
    </tr>
  );
}

export default function App() {
  const [issues, setIssues] = useState<Issue[]>([]);
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [listError, setListError] = useState("");
  const [savingId, setSavingId] = useState<string | null>(null);
  const [lastSync, setLastSync] = useState<string | null>(null);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setListError("");
    try {
      const result = await getIssues(signal);
      if (signal?.aborted) return;
      setIssues(result);
      setLoaded(true);
      setLastSync(new Date().toISOString());
    } catch (error) {
      if (!signal?.aborted) setListError(errorMessage(error));
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    return () => controller.abort();
  }, [refresh]);

  async function saveStatus(id: string, status: IssueStatus) {
    setSavingId(id);
    try {
      const updated = await updateIssueStatus(id, status);
      // 使用后端返回的完整对象，保留后端生成的 updatedAt。
      setIssues((current) =>
        current.map((issue) => (issue.id === id ? updated : issue)),
      );
    } finally {
      setSavingId(null);
    }
  }

  const busy = loading || savingId !== null;
  return (
    <div className="app-shell">
      <header className="site-header">
        <div className="header-inner">
          <div className="brand">
            <span className="brand-mark" aria-hidden="true">
              <svg viewBox="0 0 32 32" fill="none">
                <path
                  d="m16 3 12 7v13l-12 7L4 23V10L16 3Z M4 10l12 7 12-7 M16 17v13"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  strokeLinejoin="round"
                />
              </svg>
            </span>
            <div>
              <strong>AR 巡检</strong>
              <span>问题管理端</span>
            </div>
          </div>
          <span className="course-label">Unity-AR 研发实操作业</span>
        </div>
      </header>

      <main>
        <section className="page-heading">
          <div>
            <p className="eyebrow">巡检工作台</p>
            <h1>每一个问题，都有迹可循。</h1>
            <p className="page-description">
              查看空间巡检记录，跟进问题处理进度。
            </p>
          </div>
          <button
            className="refresh-button"
            disabled={busy}
            onClick={() => void refresh()}
          >
            <span aria-hidden="true">↻</span>
            {loading ? "正在加载…" : "刷新问题列表"}
          </button>
        </section>

        <section className="stats" aria-label="问题统计">
          <div className="stat-card">
            <p>全部问题</p>
            <strong>{loaded ? issues.length : "—"}</strong>
            <span>巡检记录总数</span>
          </div>
          {statuses.map((status) => (
            <div className={`stat-card stat-${status}`} key={status}>
              <p>
                <i />
                {statusLabels[status]}
              </p>
              <strong>
                {loaded
                  ? issues.filter((issue) => issue.status === status).length
                  : "—"}
              </strong>
              <span>{status}</span>
            </div>
          ))}
        </section>

        <section
          className="issues-panel"
          aria-labelledby="issues-title"
          aria-busy={loading}
        >
          <div className="panel-heading">
            <div>
              <h2 id="issues-title">
                巡检问题列表 <span>{loaded ? issues.length : "—"}</span>
              </h2>
              <p>状态修改后点击保存，结果以服务端返回为准。</p>
            </div>
            <span
              className={`connection ${listError ? "connection-error" : ""}`}
            >
              <i />
              {loading
                ? "正在获取数据"
                : listError
                  ? "获取数据失败"
                  : loaded
                    ? "已读取后端数据"
                    : "等待连接"}
            </span>
          </div>

          {listError && (
            <div className="error-banner" role="alert">
              <strong>
                {loaded
                  ? "刷新失败，仍显示上次成功获取的数据。"
                  : "暂时无法获取问题列表。"}
              </strong>
              <p>{listError}</p>
              <button disabled={busy} onClick={() => void refresh()}>
                重新获取
              </button>
            </div>
          )}
          {loading && (
            <div className="loading-message" role="status">
              <span className="spinner" />
              正在从后端加载巡检问题…
            </div>
          )}
          {loaded && issues.length > 0 && (
            <div className="table-scroll">
              <table>
                <caption className="sr-only">
                  巡检问题、优先级、状态、坐标、创建及更新时间与状态操作
                </caption>
                <thead>
                  <tr>
                    <th scope="col">问题详情</th>
                    <th scope="col">优先级</th>
                    <th scope="col">处理状态</th>
                    <th scope="col">空间坐标</th>
                    <th scope="col">记录时间</th>
                    <th scope="col">状态操作</th>
                  </tr>
                </thead>
                <tbody>
                  {issues.map((issue) => (
                    <IssueRow
                      key={issue.id}
                      issue={issue}
                      busy={busy}
                      saving={savingId === issue.id}
                      onSave={saveStatus}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {loaded && issues.length === 0 && !loading && !listError && (
            <div className="empty-state">
              <span aria-hidden="true">◇</span>
              <h3>暂无巡检问题</h3>
              <p>后端尚未保存问题。提交问题后，点击“刷新问题列表”查看。</p>
            </div>
          )}
          <div className="panel-footer">
            <span>
              数据来源：Go 后端{listError && loaded ? "（上次成功读取）" : ""}
            </span>
            <span>
              {lastSync
                ? `上次完整刷新：${formatTime(lastSync)}`
                : "尚未完成首次读取"}
            </span>
          </div>
        </section>
        <footer className="page-footer">
          <span>AR 发现问题 · 管理端跟进处理</span>
          <span>
            API：<code>{apiBaseURL}</code>
          </span>
        </footer>
      </main>
    </div>
  );
}
