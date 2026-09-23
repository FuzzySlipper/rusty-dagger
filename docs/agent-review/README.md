# Agent review workflow

Status: active convention, 2026-09-11. The Den project document
`rusty-dagger/agent-review-workflow` owns the policy; the files here are the
packets handed to reviewers. Where the two disagree, Den wins.

Reviewers are persistent agents, not one-shot checks. An identified issue is
re-checked by the same reviewer in the same session, so round two verifies the
fix instead of rediscovering the problem from a blank context.

This is not an approval system and not an interactive gate. Reviewers report
source-backed findings; the root agent reconciles them and decides.

## Lane roster

Three lanes run on **every** task (the third is a temporary counterbalance —
see its lane file for the sunset rule):

| Lane | Packet |
| --- | --- |
| Engine reuse — upstream reinvention | [lanes/engine-reuse.md](lanes/engine-reuse.md) |
| Existing product reuse — repo-local reinvention, placement, and mechanism shape | [lanes/existing-product-reuse.md](lanes/existing-product-reuse.md) |
| Runtime trust — validation ceremony on trusted paths | [lanes/runtime-trust.md](lanes/runtime-trust.md) |

The reuse lanes are always on because agents skip capabilities that already
exist. New code gets written for something the Engine already guarantees, or for
something rusty-dagger already owns, instead of extending it. Runtime trust is
always on for the opposite failure: agents add checking the runtime does not
need. Import-side validation gravity (bounds, provenance, digests) leaks into
trusted single-player runtime paths as propose/validate/mutate steps, repeated
hash admission, revision guards, snapshots, and rollback. The recent refactors
removed that machinery; this lane holds the removal until runtime gravity is
established (see `docs/gameplay-design.md` and rusty-engine Board post 147).

Optional lanes. In DSH, pick to a total of three to four reviewers, and pick lanes
whose questions can disagree with each other. Codex/Prime mapping is below:

| Lane | Use when |
| --- | --- |
| [Ownership and values](lanes/ownership-and-values.md) | the change crosses an ownership seam, adds tuning, or touches Daggerfall vocabulary |
| [Behavior and interoperability](lanes/behavior-and-interoperability.md) | the task specifies behavior, real callers, persistence, or save/UI contracts |
| [Requirement and acceptance](lanes/requirement-and-acceptance.md) | the task carries explicit acceptance criteria |
| [Error and boundary paths](lanes/error-and-boundary-paths.md) | the change adds parsing, input handling, or failure paths |
| [Test claims](lanes/test-claims.md) | the change adds or edits tests, or claims verification |

Do not open a lane that repeats another lane's question in different words. Do
not run the full roster to be safe. Under the DSH roster, a trivial task is three
reviewers, and a task that changes a boundary, a save contract, or an ownership
seam is four. Codex/Prime uses the standing partners below instead of adding this
roster on top of them.

## DSH agents: choosing the reviewer tool

The following tools, context behavior, settlement notices, and settled-reviewer
messaging are valid for the DSH harness. They are not Codex tool names.

| Tool | Context | Use for |
| --- | --- | --- |
| `subagent_review` | fresh; does not see the conversation | adversarial and requirement lanes, where anchoring on the root agent's reasoning would weaken the check |
| `subagent_audit` | inherits the root agent's completed turns | lanes that need the change's rationale — reuse, ownership, interoperability, runtime trust |

Open every reviewer for a round in one message, one reviewer per lane, and keep
working while they run. Their reports arrive as settlement notices. Do not wait
and do not poll.

Give a fresh reviewer everything it needs: repository path, the exact artifact
under review, and the command that demonstrates the behavior. Name the lane
packet explicitly in the prompt; a reviewer gets the generic packet plus its own
lane file, never the other lanes'.

### DSH revision rounds

When a round's findings are addressed, `send_message` the same reviewer. State
what changed, what was deliberately left alone and why, and which finding ids to
re-check. The reviewer keeps its own history, so it can judge whether the fix
resolved the issue it actually raised.

Never open a new reviewer for a round of work an existing reviewer has already
seen; that discards the continuity that makes the second pass worth reading. A
settled reviewer is still a valid `send_message` target. Recover ids with
`list_agents` after a long session, and use `interrupt_agent` on a reviewer whose
lane no longer matters.

Open a fresh reviewer only when the revision is large enough that the old
reviewer's accumulated position is itself a source of bias, and say so when you
do.

## Codex agents

Use Codex's available collaboration tools, not DSH's `subagent_review` or
`subagent_audit`. Reuse persistent sessions across tasks and review fixes.
With `collaboration`, use `spawn_agent` for initial assignments, `send_message`
for active-agent steering, and `followup_task` to start another assignment on an
idle or completed agent. An idle-agent message alone does not start a turn.
Use the documented equivalent when another Codex runtime exposes different tools.

When Prime is selected, its standing team satisfies the review responsibilities:

| Responsibility | Persistent Prime partner |
| --- | --- |
| Engine reuse | Upstream checker: `gpt-6-luna`, `max` |
| Existing product reuse and unfinished task paths | Reuse checker: `gpt-6-luna`, `max` |
| Runtime trust, correctness, and relevant optional review questions | Senior: `gpt-6-astra`, `medium` |

Do not add a second roster or a fourth reviewer solely because the DSH rule
calls for one. Assign relevant lane questions to these partners; add another
reviewer only for a concrete independent question the standing team cannot
usefully cover. Without Prime, retain the required review responsibilities with
the available persistent Codex partners and the user's selected model settings.

Give each partner the generic packet and the lane guidance relevant to its
assigned questions. Prime's Senior may cover more than one complementary
question; keep ownership explicit and avoid duplicate investigations. Preserve
the main agent's implementation ownership and final judgment.

Keep useful independent work moving during review. Use bounded waits when
needed, avoid busy polling, and collect required results before claiming review
completion. Send fixes back through `followup_task` to the same idle reviewer,
with changed paths and finding IDs. Keep the standing sessions for the next
task; replace only an unavailable session or one with a concrete context problem.

Respect live slot limits. Defer optional helpers or do their work at the root
rather than discarding a standing reviewer. State an unavailable review lane
honestly; do not claim it ran. The selected Codex/Prime workflow does not launch
an external Den review by default; honor an explicit task-owned external gate.

## Authority on disagreement

Findings are claims to verify, not instructions to apply.

- A factual dispute is settled with evidence and a re-check in the reviewer's own
  session. That exchange is the point of persistence.
- A scope dispute is not the reviewer's call. The task's stated contract and the
  user decide; the root records the disposition and the reason.
- A reviewer's verdict never amends user intent, and an unresolved finding is
  never silently dropped — it is deferred explicitly, or declined with a reason.

## What reviewers must not report

Stylistic preferences, new scope, broad redesign proposals, invented acceptance
criteria, or interactive gates. A finding that is not backed by a file and line,
command output, or a command that reproduces it does not belong in the report.
