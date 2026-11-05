using System.CommandLine;
using System.CommandLine.Help;
using Narrative.App.Data;
using Narrative.App.Export;
using Narrative.App.Mail;
using Narrative.App.Queries;
using Narrative.App.Sources;
using Narrative.App.WhatsApp;

namespace Narrative.App
{
    /// <summary>
    /// The command line. Steps run in this order: -init, -alias, -import, the list options, -export,
    /// then -show-template.
    /// </summary>
    public static class Cli
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            var initOption = new Option<bool>("-init") { Description = "Create a new, empty database." };
            var forceOption = new Option<bool>("-force") { Description = "With -init, replace an existing database." };
            var dbNameOption = new Option<string>("-dbname")
            {
                Description = "Database file to use.",
                HelpName = "file",
                DefaultValueFactory = _ => NarrativeDatabase.DefaultFileName,
            };
            var importOption = new Option<string[]>("-import")
            {
                Description = @"Files to import: WhatsApp chat .txt files (attachments are read from the same folder), WhatsApp .zip exports, email .eml and .mbox files, or folders of any of these. Wildcards are allowed in the file name, e.g. exports\*.zip.",
                HelpName = "paths",
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true,
            };
            var chatOption = new Option<string?>("-chat")
            {
                Description = "With -import, the chat name to use instead of deriving it from the file name (for email, put every email in this one conversation instead of its thread). With -list-people, list only that chat's people. With -export, the chat or email thread to export.",
                HelpName = "name",
            };
            var aliasOption = new Option<string[]>("-alias")
            {
                Description = "Make the first name, or an email address, another name for the person called the second, so imports treat both as one person (e.g. after a name change). Set name aliases before importing WhatsApp chats; people with only email can be merged at any time.",
                HelpName = "alias> <name",
                Arity = new ArgumentArity(2, 2),
                AllowMultipleArgumentsPerToken = true,
            };
            var listChatsOption = new Option<bool>("-list-chats") { Description = "List chat names." };
            var personOption = new Option<string[]>("-person")
            {
                Description = "With -list-chats, list only the chats that include this person. With -export and -chat, export only these people's messages (system messages are then left out). With -export and no -chat, give two or more people to export the conversation between them across every chat and email.",
                HelpName = "names",
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true,
            };
            var listPeopleOption = new Option<bool>("-list-people") { Description = "List people, with their aliases and email addresses." };
            var sourceOption = new Option<string?>("-source")
            {
                Description = "With -list-chats or -export, only whatsapp or only email.",
                HelpName = "source",
            };
            var directOption = new Option<bool>("-direct")
            {
                Description = "With -export of a conversation between people (no -chat), leave out group chats and emails that went to anyone else.",
            };
            var exportOption = new Option<string?>("-export")
            {
                Description = "Export the chat or email thread named by -chat, or the conversation between the -person names, to this file. The format comes from the extension unless -format or -template says otherwise: .json, .xml, .md, .html, .csv, .jsonl (one line per message, sender and text only), or .txt or .zip for WhatsApp's own export format (a .zip holds the chat file and attachments, laid out as WhatsApp does).",
                HelpName = "file",
            };
            var formatOption = new Option<string?>("-format") { Description = "With -export: json, xml, md, html, csv, jsonl or whatsapp (also txt).", HelpName = "format" };
            var templateOption = new Option<string?>("-template")
            {
                Description = "With -export, render through this Scriban template instead of a built-in format. Start from -show-template.",
                HelpName = "file",
            };
            var attachmentsOption = new Option<string>("-attachments")
            {
                Description = "With -export, what to do with stored attachment files: folder (write them to a <name>_files folder beside the export; for a WhatsApp .txt, beside it under their own names), embed (inside the export; not for WhatsApp, CSV or JSONL), or none. A CSV or JSONL export never writes files. A WhatsApp .zip holds them unless this is none.",
                HelpName = "mode",
                DefaultValueFactory = _ => "folder",
            };
            var fromOption = new Option<string?>("-from")
            {
                Description = "With -export, the first date or time to include: yyyy-MM-dd or \"yyyy-MM-dd HH:mm\".",
                HelpName = "date",
            };
            var toOption = new Option<string?>("-to")
            {
                Description = "With -export, the last date to include (the whole day), or a time to stop before: yyyy-MM-dd or \"yyyy-MM-dd HH:mm\".",
                HelpName = "date",
            };
            var excelOption = new Option<bool>("-excel")
            {
                Description = "With a .csv export, write it for Excel: add a BOM so Excel reads UTF-8, and put an apostrophe before values Excel would take for formulas (starting with = + - or @, such as @mentions and +44 phone-number names).",
            };
            var meOption = new Option<string?>("-me")
            {
                Description = "With -export, show this person's messages as your own: on the right in HTML.",
                HelpName = "name",
            };
            var showTemplateOption = new Option<string?>("-show-template")
            {
                Description = "Write the built-in md or html template to stdout, as a starting point for -template.",
                HelpName = "format",
            };
            var quietOption = new Option<bool>("-quiet") { Description = "Don't write progress to stdout. Errors and warnings still go to stderr." };

            var root = new RootCommand("narrative: aggregates WhatsApp chat exports and emails into a SQLite database, and exports conversations from it.")
            {
                initOption,
                forceOption,
                dbNameOption,
                importOption,
                aliasOption,
                chatOption,
                listChatsOption,
                personOption,
                listPeopleOption,
                sourceOption,
                exportOption,
                formatOption,
                templateOption,
                attachmentsOption,
                fromOption,
                toOption,
                meOption,
                directOption,
                excelOption,
                showTemplateOption,
                quietOption,
            };
            // The built-in options use double dashes; give them single-dash spellings to match ours.
            root.Options.OfType<HelpOption>().Single().Aliases.Add("-help");
            root.Options.OfType<VersionOption>().Single().Aliases.Add("-version");

            root.SetAction(parseResult =>
            {
                var status = new StatusWriter(output, error, parseResult.GetValue(quietOption));
                var dbName = parseResult.GetValue(dbNameOption)!;
                var importSpecs = parseResult.GetValue(importOption) ?? Array.Empty<string>();
                var init = parseResult.GetValue(initOption);
                var listChats = parseResult.GetValue(listChatsOption);
                var listPeople = parseResult.GetValue(listPeopleOption);
                var people = parseResult.GetValue(personOption) ?? Array.Empty<string>();
                var exportPath = parseResult.GetValue(exportOption);
                var showTemplate = parseResult.GetValue(showTemplateOption);
                var alias = parseResult.GetValue(aliasOption) ?? Array.Empty<string>();

                if (!init && alias.Length == 0 && importSpecs.Length == 0 && !listChats && !listPeople && exportPath == null && showTemplate == null)
                {
                    status.Error("Nothing to do. Use -init, -alias, -import, -list-chats, -list-people, -export or -show-template. Use -h for help.");
                    return 1;
                }

                try
                {
                    if (init)
                    {
                        NarrativeDatabase.Create(dbName, parseResult.GetValue(forceOption));
                        status.Info($"Created {dbName}");
                    }
                    if (alias.Length > 0)
                    {
                        AddAlias(dbName, alias[0], alias[1], status);
                    }
                    if (importSpecs.Length > 0)
                    {
                        Import(dbName, importSpecs, parseResult.GetValue(chatOption), status);
                    }
                    if (listChats)
                    {
                        if (people.Length > 1)
                        {
                            throw new NarrativeException("-list-chats takes one -person.");
                        }
                        ListChats(dbName, people.FirstOrDefault(), parseResult.GetValue(sourceOption), output);
                    }
                    if (listPeople)
                    {
                        ListPeople(dbName, parseResult.GetValue(chatOption), output);
                    }
                    if (exportPath != null)
                    {
                        var request = ExportRequestFrom(
                            exportPath,
                            parseResult.GetValue(chatOption),
                            people,
                            parseResult.GetValue(formatOption),
                            parseResult.GetValue(templateOption),
                            parseResult.GetValue(attachmentsOption)!,
                            parseResult.GetValue(fromOption),
                            parseResult.GetValue(toOption),
                            parseResult.GetValue(meOption),
                            parseResult.GetValue(excelOption),
                            parseResult.GetValue(sourceOption),
                            parseResult.GetValue(directOption));
                        Export(dbName, request, status);
                    }
                    if (showTemplate != null)
                    {
                        var format = ExportRequest.ParseFormat(showTemplate);
                        if (format != ExportFormat.Markdown && format != ExportFormat.Html)
                        {
                            throw new NarrativeException("-show-template takes md or html.");
                        }
                        output.Write(TemplateExporter.BuiltInTemplate(format.Value));
                    }
                    return 0;
                }
                catch (NarrativeException ex)
                {
                    status.Error(ex.Message);
                    return 1;
                }
            });

            // Response files are off: chat names can start with "@" (a chat with yourself is named
            // after your @handle), and "@name" would otherwise be read as a file of arguments.
            var parserConfiguration = new ParserConfiguration { ResponseFileTokenReplacer = null };
            return root.Parse(args, parserConfiguration).Invoke(new InvocationConfiguration { Output = output, Error = error });
        }

        private static void Import(string dbName, IEnumerable<string> specs, string? chatName, StatusWriter status)
        {
            NarrativeDatabase.Open(dbName).Dispose();

            using var exports = ExportSources.Resolve(specs, AndroidUkHeaderFormat.Instance);
            int added = 0, matched = 0, updated = 0, linked = 0, missing = 0;
            foreach (var source in exports.Sources)
            {
                // A fresh context per file, so a failed file leaves nothing behind for the next one.
                using var db = NarrativeDatabase.Open(dbName);
                var result = new WhatsAppImporter(db).Import(source, chatName);
                switch (result.Status)
                {
                    case ImportStatus.AlreadyImported:
                        status.Info($"{result.FileName}: already imported{AttachmentSummary(result)}");
                        break;
                    case ImportStatus.Empty:
                        status.Warning($"{result.FileName}: no messages found, skipped");
                        break;
                    default:
                        status.Info($"{result.FileName}: chat '{result.ChatName}', {result.Parsed} messages: " +
                                    $"{result.Added} new, {result.Matched} already stored, {result.Updated} updated" +
                                    AttachmentSummary(result));
                        added += result.Added;
                        matched += result.Matched;
                        updated += result.Updated;
                        break;
                }
                linked += result.AttachmentsLinked;
                missing += result.AttachmentsMissing;
                if (result.OrphanLines > 0)
                {
                    status.Warning($"{result.FileName}: ignored {result.OrphanLines} line(s) before the first message");
                }
                if (result.UnreferencedFiles > 0)
                {
                    status.Warning($"{result.FileName}: {result.UnreferencedFiles} file(s) in the zip aren't named by any message, not stored");
                }
            }
            foreach (var path in exports.EmailFiles)
            {
                using var db = NarrativeDatabase.Open(dbName);
                var result = new EmailImporter(db).Import(path, chatName);
                switch (result.Status)
                {
                    case ImportStatus.AlreadyImported:
                        status.Info($"{result.FileName}: already imported");
                        break;
                    case ImportStatus.Empty:
                        status.Warning($"{result.FileName}: no emails found, skipped");
                        break;
                    default:
                        status.Info($"{result.FileName}: {result.Parsed} email(s): {result.Added} new, {result.Matched} already stored" +
                                    (result.Updated > 0 ? $" ({result.Updated} with more recipients)" : "") +
                                    (result.AttachmentsLinked > 0 ? $"; {result.AttachmentsLinked} attachments" : ""));
                        added += result.Added;
                        matched += result.Matched;
                        updated += result.Updated;
                        linked += result.AttachmentsLinked;
                        break;
                }
                foreach (var warning in result.Warnings)
                {
                    status.Warning(warning);
                }
            }
            if (exports.Sources.Count + exports.EmailFiles.Count > 1)
            {
                status.Info($"Total: {added} new, {matched} already stored, {updated} updated; " +
                            $"{linked} attachments stored, {missing} missing");
            }
        }

        private static string AttachmentSummary(ImportResult result)
        {
            if (result.AttachmentsReferenced == 0)
            {
                return "";
            }
            return $"; {result.AttachmentsReferenced} attachments named: {result.AttachmentsLinked} newly stored, " +
                   $"{result.AttachmentsMissing} missing from this export";
        }

        private static ExportRequest ExportRequestFrom(string path, string? chat, string[] people, string? formatName,
            string? templatePath, string attachments, string? from, string? to, string? me, bool excel, string? source, bool direct)
        {
            ExportFormat format;
            if (templatePath != null)
            {
                if (formatName != null)
                {
                    throw new NarrativeException("Use -format or -template, not both.");
                }
                format = ExportFormat.Template;
            }
            else if (formatName != null)
            {
                format = ExportRequest.ParseFormat(formatName)
                    ?? throw new NarrativeException($"Unknown format '{formatName}'. Use json, xml, md, html, csv, jsonl or whatsapp.");
            }
            else
            {
                format = ExportRequest.FormatFromExtension(path)
                    ?? throw new NarrativeException(
                        $"Can't tell the format from '{Path.GetFileName(path)}'. Use a .json, .xml, .md, .html, .csv, .jsonl, .txt or .zip file name, or -format.");
            }

            var mode = attachments.ToLowerInvariant() switch
            {
                "folder" => AttachmentMode.Folder,
                "embed" => AttachmentMode.Embed,
                "none" => AttachmentMode.None,
                _ => throw new NarrativeException($"Unknown -attachments mode '{attachments}'. Use folder, embed or none."),
            };

            var fromTime = from == null ? (DateTime?)null : ParseTime(from, "-from").Time;
            DateTime? toTime = null;
            if (to != null)
            {
                var (time, dateOnly) = ParseTime(to, "-to");
                // A date on its own means "up to and including that day".
                toTime = dateOnly ? time.AddDays(1) : time;
            }
            if (fromTime != null && toTime != null && fromTime >= toTime)
            {
                throw new NarrativeException("-from must be before -to.");
            }

            return new ExportRequest
            {
                Chat = chat,
                Source = source,
                Direct = direct,
                People = people,
                From = fromTime,
                To = toTime,
                Me = me,
                Excel = excel,
                Format = format,
                TemplatePath = templatePath,
                Attachments = mode,
                OutputPath = path,
            };
        }

        private static (DateTime Time, bool DateOnly) ParseTime(string text, string option)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var styles = System.Globalization.DateTimeStyles.None;
            if (DateTime.TryParseExact(text, "yyyy-MM-dd", culture, styles, out var date))
            {
                return (date, true);
            }
            if (DateTime.TryParseExact(text, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm" }, culture, styles, out var time))
            {
                return (time, false);
            }
            throw new NarrativeException($"{option} '{text}' isn't a date. Use yyyy-MM-dd or \"yyyy-MM-dd HH:mm\".");
        }

        private static void Export(string dbName, ExportRequest request, StatusWriter status)
        {
            using var db = NarrativeDatabase.Open(dbName);
            var result = new Exporter(db).Export(request);
            var files = result.FilesFolder != null
                ? $"; {result.Attachments} attachment files written to {result.FilesFolder}"
                : result.Attachments > 0 && (request.Attachments == AttachmentMode.Embed || request.Format == ExportFormat.WhatsApp)
                    ? $"; {result.Attachments} attachment files embedded"
                    : "";
            foreach (var warning in result.Warnings)
            {
                status.Warning(warning);
            }
            var what = request.Chat != null ? $"chat '{request.Chat}'" : "the conversation between " + string.Join(", ", request.People.Select(p => $"'{p}'"));
            status.Info($"Exported {result.Messages} messages from {what} to {request.OutputPath}{files}");
        }

        private static void ListChats(string dbName, string? person, string? source, TextWriter output)
        {
            using var db = NarrativeDatabase.Open(dbName);
            foreach (var chat in new NarrativeQueries(db).ListChats(person, source == null ? null : ExportBuilder.SourceName(source)))
            {
                var range = chat.First == null ? "" : $", {chat.First:yyyy-MM-dd} to {chat.Last:yyyy-MM-dd}";
                output.WriteLine($"{chat.Name} ({chat.Source}, {chat.MessageCount} messages{range})");
            }
        }

        private static void ListPeople(string dbName, string? chat, TextWriter output)
        {
            using var db = NarrativeDatabase.Open(dbName);
            var queries = new NarrativeQueries(db);
            var aliases = queries.ListAliases().ToLookup(a => a.Name, a => a.Alias);
            var addresses = queries.ListAddresses().ToLookup(a => a.Name, a => a.Address);
            foreach (var name in queries.ListPeople(chat))
            {
                var line = aliases.Contains(name) ? $"{name} (also {string.Join(", ", aliases[name])})" : name;
                if (addresses.Contains(name))
                {
                    line += " " + string.Join(" ", addresses[name].Select(a => $"<{a}>"));
                }
                output.WriteLine(line);
            }
        }

        private static void AddAlias(string dbName, string alias, string name, StatusWriter status)
        {
            using var db = NarrativeDatabase.Open(dbName);
            var (person, added) = new PersonDirectory(db).AddAlias(alias, name);
            status.Info(added
                ? $"'{alias}' is now another name for '{person.DisplayName}'"
                : $"'{alias}' is already another name for '{person.DisplayName}'");
        }
    }
}
