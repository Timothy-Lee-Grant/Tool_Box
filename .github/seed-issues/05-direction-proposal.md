## Goal (Stage 1)
Write a proposal (as a Brainstorm document, following `Documentation/CONVENTIONS.md`) for the new direction of Tool_Box:

- **A control-center GUI, similar to Docker Desktop.** Each toolset gets a tab. Enabling a toolset starts whatever background process it needs, and the agent then sees that toolset's tools. Example: enable the voxel "Art Box", and the agent can place blocks. A "Show display" button pops out the world viewer; closing the viewer leaves the server and the tools running.
- **A RAG section.** I point it at documents; it indexes, embeds and stores them in a vector database, and the agent can query them. Drag and drop to organise documents into separate collections (for example "firmware docs" vs. another topic), so the agent can query one specialised collection instead of everything.

The proposal should give several architecture options (GUI technology, how the GUI talks to the MCP host, process model for toolsets, vector store choices), a list of feature ideas, and the different ways a user could interact with the system.

## Acceptance criteria
- [ ] `Documentation/Brainstorms/004-...md` written, linked here
- [ ] Includes a section on the cleanup and upgrade path (it can reference the archive and branch issues)
- [ ] Ends with the open questions I need to answer

## How far may the agent go without checking in?
Plan and implement, then ask me to review
