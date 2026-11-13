import type { UseQueryResult } from '@tanstack/react-query';
import type { ParseKeys } from 'i18next';
import { problemOf } from '../../api/problem';
import type { WorkItem, WorkItemPage } from '../../api/work-items';

type Failure = 'workItems.unreachable' | 'workItems.failed';

export const failureOf = ({ error }: UseQueryResult): Failure | undefined =>
  error
    ? problemOf(error).code === 'service_unavailable'
      ? 'workItems.unreachable'
      : 'workItems.failed'
    : undefined;

const cancelling = (item: WorkItem) => item.status === 'Running' && item.cancelRequestedAt !== null;

export const statusOf = (item: WorkItem): ParseKeys =>
  cancelling(item) ? 'workItems.cancelRequested' : `workItems.statuses.${item.status}`;

export const cancellable = (item: WorkItem) =>
  (item.status === 'Queued' || item.status === 'Running') && !cancelling(item);

export const at = (text: string) => ({
  at: new Date(text),
  formatParams: { at: { dateStyle: 'medium', timeStyle: 'short' } },
});

export const timestamps = (item: WorkItem) =>
  (['requestedAt', 'startedAt', 'finishedAt', 'cancelRequestedAt', 'updatedAt'] as const).flatMap(
    (field) => {
      const value = item[field];
      return value === null ? [] : [{ field, value }];
    },
  );

export const resultFields = (result: unknown) =>
  Object.entries(result ?? {}).map(([name, value]) => ({
    name,
    value: typeof value === 'string' ? value : JSON.stringify(value),
  }));

export const pagesAround = ({ page, pageSize, total }: WorkItemPage) => ({
  newer: page - 1 || undefined,
  older: page * pageSize < total ? page + 1 : undefined,
});

export const meter = ({ done, total }: WorkItem['progress']) =>
  total === null ? {} : { value: done, max: total, text: { done, total } };
