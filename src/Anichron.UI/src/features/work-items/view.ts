import type { UseQueryResult } from '@tanstack/react-query';
import type { ParseKeys } from 'i18next';
import { problemOf } from '../../api/problem';
import type { WorkItem, WorkItemPage } from '../../api/work-items';

type Failure = 'workItems.unreachable' | 'workItems.failed';

export function failureOf({ error }: UseQueryResult): Failure | undefined {
  return error
    ? problemOf(error).code === 'service_unavailable'
      ? 'workItems.unreachable'
      : 'workItems.failed'
    : undefined;
}

function cancelling(item: WorkItem) {
  return item.status === 'Running' && item.cancelRequestedAt !== null;
}

export function statusOf(item: WorkItem): ParseKeys {
  return cancelling(item) ? 'workItems.cancelRequested' : `workItems.statuses.${item.status}`;
}

export function cancellable(item: WorkItem) {
  return (item.status === 'Queued' || item.status === 'Running') && !cancelling(item);
}

export function at(text: string) {
  return {
    at: new Date(text),
    formatParams: { at: { dateStyle: 'medium', timeStyle: 'short' } },
  };
}

export function timestamps(item: WorkItem) {
  return (
    ['requestedAt', 'startedAt', 'finishedAt', 'cancelRequestedAt', 'updatedAt'] as const
  ).flatMap((field) => {
    const value = item[field];
    return value === null ? [] : [{ field, value }];
  });
}

export function resultFields(result: unknown) {
  return Object.entries(result ?? {}).map(([name, value]) => ({
    name,
    value: typeof value === 'string' ? value : JSON.stringify(value),
  }));
}

export function pagesAround({ page, pageSize, total }: WorkItemPage) {
  return {
    newer: page - 1 || undefined,
    older: page * pageSize < total ? page + 1 : undefined,
  };
}

export function meter({ done, total }: WorkItem['progress']) {
  return total === null ? {} : { value: done, max: total, text: { done, total } };
}
