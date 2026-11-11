# Agent review workflow

The Den project document `rusty-dagger/agent-review-workflow` owns the policy: which lanes run and why, reviewer
model routing, and who decides a disagreement. Where it and these files disagree, Den wins. The files here are the
packets handed to reviewers by path: every reviewer gets [reviewer-packet.md](reviewer-packet.md) plus its own lane
file, never the other lanes'.

## Lanes

| Lane | Run | Packet |
| --- | --- | --- |
| Engine reuse | every task | [lanes/engine-reuse.md](lanes/engine-reuse.md) |
| Existing product reuse | every task | [lanes/existing-product-reuse.md](lanes/existing-product-reuse.md) |
| Runtime trust | every task | [lanes/runtime-trust.md](lanes/runtime-trust.md) |
| Ownership and values | the change crosses an ownership seam, adds tuning, or touches Daggerfall vocabulary | [lanes/ownership-and-values.md](lanes/ownership-and-values.md) |
| Behavior and interoperability | the task specifies behavior, real callers, persistence, or save/UI contracts | [lanes/behavior-and-interoperability.md](lanes/behavior-and-interoperability.md) |
| Requirement and acceptance | the task carries explicit acceptance criteria | [lanes/requirement-and-acceptance.md](lanes/requirement-and-acceptance.md) |
| Error and boundary paths | the change adds parsing, input handling, or failure paths | [lanes/error-and-boundary-paths.md](lanes/error-and-boundary-paths.md) |
| Test claims | the change adds or edits tests, or claims verification | [lanes/test-claims.md](lanes/test-claims.md) |

In DSH, pick optional lanes to a total of three to four reviewers: three for a trivial task, four for one that
changes a boundary, a save contract or an ownership seam. Do not open a lane that repeats another lane's question,
and do not run the full roster to be safe. Codex/Prime uses its standing partners instead of this roster.

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
| Engine reuse | Upstream checker |
| Existing product reuse and unfinished task paths | Reuse checker |
| Runtime trust, correctness, and relevant optional review questions | Senior |

The Den policy document assigns each partner's model and effort.

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
