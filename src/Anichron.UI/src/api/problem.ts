import { z } from 'zod/mini';
import type { components } from './generated/v1';

export type ErrorCode = components['schemas']['ErrorCode'] | 'required';

export interface Problem {
  code: string | undefined;
  fields: Readonly<Partial<Record<string, string>>>;
  retryAfterSeconds: number;
}

function problemBody() {
  return z.catch(
    z.object({
      code: z.catch(z.optional(z.string()), undefined),
      errors: z.catch(
        z.record(
          z.string(),
          z.pipe(
            z.tuple([z.string()], z.string()),
            z.transform(([first]) => first),
          ),
        ),
        {},
      ),
    }),
    { code: undefined, errors: {} },
  );
}

export const unknownProblem: Problem = { code: undefined, fields: {}, retryAfterSeconds: 0 };

export const unreachableProblem: Problem = {
  code: 'service_unavailable' satisfies ErrorCode,
  fields: {},
  retryAfterSeconds: 0,
};

export function toProblem(error: unknown, response: Response): Problem {
  const body = problemBody().parse(error);
  return {
    code: body.code,
    fields: body.errors,
    retryAfterSeconds: Number(response.headers.get('Retry-After')) || 0,
  };
}

export function fieldCodes<F extends string>(problem: Problem, fields: readonly F[]) {
  return fields
    .filter((field) => field in problem.fields)
    .map((field) => [field, problem.fields[field]] as const);
}
