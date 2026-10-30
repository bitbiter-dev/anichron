import { z } from 'zod/mini';

export interface Problem {
  code: string | undefined;
  fields: Readonly<Partial<Record<string, string>>>;
  retryAfterSeconds: number;
}

const problemBody = z.catch(
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

export const unknownProblem: Problem = { code: undefined, fields: {}, retryAfterSeconds: 0 };

export async function readProblem(response: Response): Promise<Problem> {
  const body = problemBody.parse(await response.json().catch(() => undefined));
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
