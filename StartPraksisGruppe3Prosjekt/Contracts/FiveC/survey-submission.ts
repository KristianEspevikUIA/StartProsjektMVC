/**
 * WHAT THE FRONTEND SENDS WHEN A 5C FORM IS SUBMITTED.
 *
 * This file is the TypeScript mirror of Contracts/FiveC/SurveySubmission.cs. It is not
 * compiled by this project -- it is here so the Supabase side has the payload written in
 * a language it can paste straight into a client, an edge function or a type test.
 *
 * The C# record is what actually runs. If the two ever disagree, the C# file wins, and
 * whoever notices should fix this one in the same commit.
 *
 * The database schema is NOT defined here. Victor owns it. This only says what arrives.
 * The natural landing place is two tables -- one row per submission, one row per answer:
 *
 *   five_c_submissions (round_id, player_id, player_code, respondent_role,
 *                       respondent_user_id, question_set_version, submitted_at)
 *   five_c_answers     (submission_id -> five_c_submissions, question_key,
 *                       category_key, value)
 *
 * Two things worth carrying into whatever the schema ends up being:
 *
 *   1. One submission per (round_id, player_id, respondent_user_id). Correcting an answer
 *      updates that row; it does not add a second one. A unique constraint on those three
 *      columns is what makes that true regardless of what the client does.
 *
 *   2. `value` has to be nullable. Null means "not answered" and is not the same as 3.
 *      A NOT NULL column here quietly turns every blank into a middling opinion.
 */

/** Who is answering. The player is the subject in all three cases. */
export type RespondentRole = "player" | "coach" | "guardian";

/** One answer in a submission. */
export interface SurveyAnswer {
  /**
   * Stable key from the question set file, e.g. "commitment-1".
   * Not an index and not the question text: the text is expected to be rewritten,
   * the key is not. See Data/Questions/five-c-questions.json.
   */
  question_key: string;

  /**
   * Which of the five C's the question belongs to, e.g. "commitment".
   * Denormalised on purpose, so answers can be grouped per C without loading the
   * question set.
   */
  category_key: string;

  /**
   * The raw answer, 1-5, exactly as the respondent gave it.
   *
   * null means the question was not answered, and null is NOT 3. An unanswered question
   * is left out of every mean rather than pulled to the middle of the scale.
   *
   * Stored unreversed. Negatively worded statements are flipped when they are read
   * (6 - value), so the raw answer survives a later change to which statements count
   * as reversed.
   */
  value: number | null;
}

/** One submitted 5C form. */
export interface SurveySubmission {
  /** The round being answered. Answers outside an open round are rejected. */
  round_id: number;

  /**
   * The player the answers are ABOUT -- also when a coach or a guardian is filling the
   * form in. Never the respondent.
   */
  player_id: number;

  /**
   * The club-internal pseudonymous code, e.g. "TS-08-16". Sent so that a row is readable
   * without joining back to a table of minors. Never a name.
   */
  player_code: string;

  /** Who is answering. */
  respondent_role: RespondentRole;

  /**
   * The signed-in user who actually filled the form in. Together with round and player
   * this identifies one submission: one form per person, per player, per round.
   */
  respondent_user_id: string;

  /**
   * Which wording was on screen, from the question set file, e.g. "placeholder-2026-08-26".
   * Two rounds answered against different question texts are not comparable, and this is
   * what makes that visible afterwards.
   */
  question_set_version: string;

  /** ISO 8601 with offset, e.g. "2026-08-26T07:30:00+00:00". */
  submitted_at: string;

  /** One entry per answered question, in the order the form showed them. */
  answers: SurveyAnswer[];
}

/**
 * Example payload -- the shape a POST to the submissions endpoint carries.
 * Trimmed to three answers; a real submission carries one per question.
 */
export const exampleSubmission: SurveySubmission = {
  round_id: 2,
  player_id: 14,
  player_code: "TS-08-16",
  respondent_role: "coach",
  respondent_user_id: "9f0c1f4e-1f2a-4a5b-9a3d-7c1e2b8d4f60",
  question_set_version: "placeholder-2026-08-26",
  submitted_at: "2026-08-26T07:30:00+00:00",
  answers: [
    { question_key: "commitment-1", category_key: "commitment", value: 4 },
    { question_key: "commitment-2", category_key: "commitment", value: 5 },
    { question_key: "confidence-3", category_key: "confidence", value: null },
  ],
};
