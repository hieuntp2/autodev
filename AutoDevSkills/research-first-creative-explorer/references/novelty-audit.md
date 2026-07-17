# Anti-anchoring & the mandatory novelty audit

## Why this exists

LLMs are trained to match their data distribution, so independent ideation runs
converge on the same few themes ("mode collapse" / idea homogenization). Left
unchecked, the model returns the crowded center: SaaS, chatbots, marketplaces,
"AI for X". This file is the guardrail. Run the audit **before every answer**.

## Name and set aside the anchors (before diverging)

At the start of a creative task, explicitly list which of these are pulling you
toward the obvious, then bracket them for Phases D–E:

- the user's stored **memory** and profile
- **past/discussed projects** in this session or repo
- the user's **tech stack** and existing tools
- **Product-Hunt-style** ideas and this week's launches
- familiar **SaaS / subscription** patterns
- the **first search results** (the center, by definition)
- **trending AI** capabilities
- the assumption that **every problem needs software**

Rule: **memory and existing context are allowed only in Phase F** (fit / relevance
evaluation), never to constrain the initial idea space — unless the user explicitly
asks to build on their existing context.

## Novelty audit checklist (must pass before output)

Answer each honestly. Any "no" (or "yes" to a bad-smell item) means: generate or
recombine more, then re-audit.

1. Are the ideas different in **mechanism**, or only in name/branding?
2. Are too many ideas **SaaS / chatbot / marketplace / social network**?
3. Am I **anchored to the user's history**, stack, or past projects?
4. Is **AI used as decoration** rather than as load-bearing value?
5. Did I include at least one credible **non-software** concept?
6. Is there at least one idea that is **surprising yet logical** (makes a reader
   pause, then see the sense)?
7. Did I clearly separate **Fact / Inference / Speculation**?
8. Did I **fabricate** any market data, need, quote, or certainty? (Must be no.)
9. Did I **kill any idea too early** just because it's hard to build?
10. Can I **explain the origin of the novelty** for each strong concept (its
    novelty mechanism)?

## Diversity self-measures

Quick heuristics to catch fake diversity:

- **Class coverage:** at least one idea in each of the seven concept classes.
- **Map spread:** strong concepts land in *different* regions of the Phase C map,
  not one corner.
- **Medium mix:** not every idea is a screen/app.
- **Payer mix:** not every idea monetizes the same way (or the same payer).
- **One-line clone test:** if a concept can be summed up as "X but for Y" where X
  is a famous product, it needs another mutation pass.
