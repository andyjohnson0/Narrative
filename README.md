# Narrative

Narrative collects your personal text conversations into one local database. You can then
read the conversation between you and anyone else as a single, coherent timeline across WhatsApp
and email, and export it in whatever format you need.

It currently handles WhatsApp chat exports and email (`.eml` and mbox files).

Everything stays on your machine: Narrative is a command-line tool with a SQLite database, and it
never sends anything anywhere.

## ⚠️ This code is agent-written

Every line of code, test, and piece of documentation in this repository was written by
**Claude Opus 5.5** (Anthropic), acting as a coding agent inside Claude Code, working from
instructions given by **Andrew Johnson** ([andy@andyjohnson.uk](mailto:andy@andyjohnson.uk),
[andyjohnson.uk](https://andyjohnson.uk)), who prompted, reviewed, and directed the work.

## Why

WhatsApp only lets you export a chat as a text file, or a zip with the media. If you export the
same chat several times over the years, you end up with many overlapping files, none of them
complete:
- Older media is left out.
- Messages deleted on your phone disappear from later exports.
- Edits change the text.

Narrative merges all of them into one copy of the conversation, keeping the most complete version
of every message.

## Features

- **Import** WhatsApp exports: `.txt` files, `.zip` exports with media, or folders of them, in any
  order. Importing the same file twice does nothing.
- **Deduplication** across overlapping exports:
  - Each message is matched with its copy in other exports, even when the copies differ: media
    left out in one and present in another, edited text, or deleted placeholders.
  - Identical messages sent in the same minute are kept apart.
  - The most complete version of each message wins.
- **Attachments** are stored in the database, once each, whatever their names. Missing files are
  tolerated, and a later import can fill them in.
- **Email**: `.eml` and mbox files, in any order. Each email is stored once however many copies
  you import (sent and received copies, an `.eml` and an mbox), with recipients combined. Email
  threads are treated like chats. The body is kept as text. The quoted copy of earlier emails at
  the end of a reply (the `>` lines and the "On ..., X wrote:" line above them) is left out, so a
  thread doesn't repeat itself in the timeline; quotes with your own text after them, as in
  inline replies, are kept. Attachments are stored like WhatsApp's.
- **People across sources**: a sender is matched by email address, then by name, so the Sam
  Taylor in your WhatsApp chats is the Sam Taylor who emails you.
- **Aliases** for people who changed their name (`-alias "Sam Taylor" "Samuel Taylor"`), so
  exports made before and after the change still merge, and for extra email addresses
  (`-alias sam@example.org "Sam Taylor"`).
- **Export** the conversation between two people across WhatsApp and email, or a whole chat or
  thread, or some people's messages, between two dates:

  | Format | Notes |
  |---|---|
  | HTML | Chat bubbles, light and dark themes, colour per sender, inline images, video and audio. Copes with very large chats. `-me` puts your own messages on the right. |
  | Markdown | One section per day. |
  | JSON, XML | Every field, versioned, for other programs. |
  | CSV | One line per message: timestamp, person, text, kind, edited, attachment, source, chat, subject. `-excel` adds a BOM and guards against formula injection. |
  | JSONL | One line per message, `{"userid":"Sam Taylor","content":"..."}`, with only what was said: no system messages, deleted messages, media without a caption, timestamps, subjects or email quotes. For feeding conversations to other programs, such as language models. |
  | WhatsApp `.txt` / `.zip` | WhatsApp's own format and zip layout, so a merged chat can be written back out as one canonical export. Re-importing it gives an identical database. Email is written the same way (subject as the first line, times to the minute), so a conversation across sources reads as one chat; imported back, it's a WhatsApp chat, not emails. |
  | Your own template | HTML and Markdown come from [Scriban](https://github.com/scriban/scriban) templates. `-show-template` prints the built-in one for you to change. |

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- WhatsApp exports from **Android with a UK date format** (`dd/MM/yyyy, HH:mm - Sender: text`).
  Other phones and locales use different formats and aren't supported yet.

## Build and test

```bash
dotnet build
dotnet test
```

To build a standalone executable:

```bash
dotnet publish Narrative.App -c Release -o publish
```

The executable is `narrative` (`narrative.exe` on Windows).

## Quick start

```bash
# Create a database (narrative.db in the current folder, or use -dbname <file>)
narrative -init

# Import every export in a folder, in any order, and your email
narrative -import "exports\*.zip" "mail\*.eml" mail\archive.mbox

# See what's there
narrative -list-chats
narrative -list-people -chat "Sam Taylor"

# Export everything between you and Sam, across WhatsApp and email
narrative -export sam.html -person "Alex Smith" -person "Sam Taylor" -me "Alex Smith"

# Or one chat
narrative -export sam-chat.html -chat "Sam Taylor" -me "Alex Smith"
narrative -export sam-2023.md -chat "Sam Taylor" -from 2023-01-01 -to 2023-12-31
narrative -export sam.csv -chat "Sam Taylor" -person "Sam Taylor"
narrative -export sam.jsonl -person "Alex Smith" -person "Sam Taylor"
narrative -export merged.zip -chat "Sam Taylor"
```

Run `narrative -help` for every option.

## Commands

| Option | What it does |
|---|---|
| `-init [-force]` | Create a new database. `-force` replaces an existing one. |
| `-alias <alias> <name>` | Make one name, or an email address, another name for a person. Set name aliases before importing WhatsApp chats. |
| `-import <paths>` | Import WhatsApp `.txt` files and `.zip` exports, `.eml` and `.mbox` files, or folders of them. Wildcards work. `-chat <name>` overrides the chat name (for email, puts every email in that one conversation). |
| `-list-chats [-person <name>] [-source whatsapp\|email]` | List chats and email threads, optionally only those that include a person, or from one source. |
| `-list-people [-chat <name>]` | List people with their aliases and addresses, optionally only those in a chat. |
| `-export <file> -person <a> -person <b>` | Export the conversation between these people across every chat and email. `-direct` leaves out group chats and emails to anyone else; `-source` keeps one source. The format comes from the file's extension, or `-format`. |
| `-export <file> -chat <name>` | Export one chat or email thread. `-person` keeps only those people's messages. |
| `-from <date>`, `-to <date>` | With `-export`, a date range (`yyyy-MM-dd` or `"yyyy-MM-dd HH:mm"`). A `-to` date includes that whole day. |
| `-attachments folder\|embed\|none` | With `-export`, write files beside the export, embed them, or leave them out. |
| `-me <name>` | With `-export`, show that person's messages as your own. |
| `-excel` | With a CSV export, make it Excel-friendly. |
| `-template <file>`, `-show-template md\|html` | Export through your own template, starting from a built-in one. |
| `-dbname <file>`, `-quiet` | Choose the database file. Hide progress output. |

## Writing templates

The HTML and Markdown exports are [Scriban](https://github.com/scriban/scriban/blob/master/doc/language.md)
templates, and you can write your own for any text format. The easiest start is a built-in one:

```bash
narrative -show-template html > mine.html.scriban
narrative -export sam.html -chat "Sam Taylor" -template mine.html.scriban
```

With `-template`, the output file's extension doesn't matter, so a template can produce anything
from plain text to LaTeX. A small example that writes one line per message under day headings:

```
{{~ for day in days ~}}
== {{ object.format day.date "dddd d MMMM yyyy" }} ==
{{~ for m in day.messages ~}}
{{ object.format m.timestamp "HH:mm" }} {{ m.sender ?? "*" }}: {{ if m.kind == "media" }}[{{ m.attachment_name ?? "media" }}] {{ end }}{{ m.text | string.replace "\n" " / " }}
{{~ end ~}}
{{~ end ~}}
```

**What a template sees.** Names are in snake_case.

| Name | What it is |
|---|---|
| `title` | The chat's name, or "A and B" for a conversation between people. |
| `chat` | `chat.source` and `chat.name` when exporting one chat (`-chat`); otherwise null. |
| `selection` | What was asked for: `people`, `from`, `to` (exclusive), `me`, `source`, `direct`. |
| `people` | The senders' names, in order. A message's `sender_number` is its sender's position here, from 1. |
| `messages` | Every message, in time order. |
| `days` | The same messages grouped by day: each has `date` and `messages`. |
| `attachments` | Each stored file once: `file_name`, `media_type`, `kind`, `size`, `sha256`, `path`. |
| `first`, `last`, `exported_at` | Dates and times (`first`/`last` are null if nothing was exported). |

Each message has:

| Name | What it is |
|---|---|
| `timestamp` | UK local time: to the minute for WhatsApp, to the second for email. |
| `sender` | The person's main name, or null for a system message. |
| `is_me` | True if the sender is the `-me` person. |
| `kind` | `text`, `system`, `media` or `deleted`. |
| `text` | The text, with line breaks. For media, the caption, which may be empty. |
| `is_edited` | True if the message was edited. |
| `source`, `chat` | `WhatsApp` or `Email`, and the name of the chat or email thread it's in. |
| `attachment_state` | For media: `stored`, `missing` (named but never supplied) or `omitted` (left out of the export). |
| `attachment_name`, `attachment` | The file's name, if known, and the stored file, if any. |
| `email` | For email: `subject`, `message_id`, `date` (with the sender's offset), `to`, `cc`, `bcc`. Null for chats. |
| `email_start` | True on an email's first message, which is where to show its subject. |

**Functions.** As well as Scriban's own functions (`html.escape`, `string.replace` and so on):
- `object.format value "format"` formats a date with [.NET format strings](https://learn.microsoft.com/dotnet/standard/base-types/custom-date-and-time-format-strings).
- `attachment_url a` gives a link to a stored file: its path beside the export with
  `-attachments folder`, a `data:` URI with `-attachments embed`, or null with `-attachments none`.
- `markdown_escape text` escapes Markdown syntax and keeps line breaks.

**Tips.**
- `{{~` and `~}}` remove the whitespace before or after a tag, so lines holding only control tags
  don't leave blank lines.
- Escape text for the format you're writing: `html.escape` for HTML, `markdown_escape` for Markdown.
- A template is checked before anything is written, and an export that fails part-way deletes its
  output, so a mistake never leaves a half-written file.

## Limitations

- **Android, UK date format only** for WhatsApp, for now.
- **WhatsApp exports only show the members of a group who post**, so a member who never posted
  isn't known to be in it, and their conversation with others there isn't found.
- **Email keeps the text, not the look**: the HTML version, headers other than the date, subject,
  sender and recipients, and images embedded in HTML (such as signature logos) aren't kept.
- **Only `>` quotes are trimmed from replies.** Clients that quote without `>` marks (Outlook's
  "-----Original Message-----" or "From: ... Sent: ..." style) keep the whole earlier email.
- **Email times are converted to UK time**, the zone the WhatsApp exports use.
- **Replies and reactions aren't in WhatsApp's exports.** A reply is exported as an ordinary
  message, so reply threads can't be rebuilt.
- **WhatsApp times are to the minute**, and are local time as the phone showed them, with no time zone.
- **WhatsApp leaves older media out of exports**, so some attachments may never be available.
- **Prototype:** the database schema can still change. A database made by an older version must be
  re-created (`-init -force`) and the exports imported again; Narrative tells you when.

## Privacy

Your database holds your messages, so keep it somewhere private. `.gitignore` excludes `*.db`
files so they can't be committed by accident. The tests use generated chats, never real ones.

## Authors

- **Andrew Johnson** ([andyjohnson.uk](https://andyjohnson.uk)): the idea, design decisions,
  prompting, testing, and review.
- **Claude** (Claude Opus 5.5, by [Anthropic](https://www.anthropic.com)): co-author.

**Claude wrote all of the code in this repository**, including the tests, working in
[Claude Code](https://claude.com/claude-code). Andrew directed the work through prompts and
reviewed the results. See "This code is agent-written" at the top.

## Licence

[MIT](LICENSE.md). Third-party packages and their licences are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
