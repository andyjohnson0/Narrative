using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Narrative.App.Data
{
    public class NarrativeDbContext : DbContext
    {
        private readonly string dbPath;

        public NarrativeDbContext(string dbPath)
        {
            this.dbPath = dbPath;
        }

        public DbSet<Person> People => Set<Person>();

        public DbSet<PersonName> PersonNames => Set<PersonName>();

        public DbSet<PersonAddress> PersonAddresses => Set<PersonAddress>();

        public DbSet<Email> Emails => Set<Email>();

        public DbSet<EmailRecipient> EmailRecipients => Set<EmailRecipient>();

        public DbSet<Conversation> Conversations => Set<Conversation>();

        public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();

        public DbSet<Message> Messages => Set<Message>();

        public DbSet<ImportFile> ImportFiles => Set<ImportFile>();

        public DbSet<MessageSource> MessageSources => Set<MessageSource>();

        public DbSet<Attachment> Attachments => Set<Attachment>();

        public DbSet<AttachmentData> AttachmentData => Set<AttachmentData>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Pooling is off so the file is released on dispose, which lets -init -force replace it.
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Pooling = false,
            }.ToString();
            optionsBuilder.UseSqlite(connectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Person>()
                .HasIndex(p => p.DisplayName)
                .IsUnique();

            modelBuilder.Entity<PersonName>(n =>
            {
                n.HasKey(x => x.Name);
                n.HasIndex(x => x.PersonId);
                n.HasOne(x => x.Person).WithMany(p => p.Names).HasForeignKey(x => x.PersonId);
            });

            modelBuilder.Entity<PersonAddress>(a =>
            {
                a.HasKey(x => x.Address);
                a.HasIndex(x => x.PersonId);
                a.HasOne(x => x.Person).WithMany(p => p.Addresses).HasForeignKey(x => x.PersonId);
            });

            modelBuilder.Entity<Conversation>(c =>
            {
                c.HasIndex(x => new { x.Source, x.Name }).IsUnique();
                c.HasIndex(x => new { x.Source, x.ThreadKey }).IsUnique();
            });

            modelBuilder.Entity<Email>()
                .HasIndex(e => e.InternetMessageId)
                .IsUnique();

            modelBuilder.Entity<EmailRecipient>(r =>
            {
                r.HasKey(x => new { x.EmailId, x.PersonId, x.Role });
                r.Property(x => x.Role).HasConversion<string>();
                r.HasIndex(x => x.PersonId);
            });

            modelBuilder.Entity<ConversationParticipant>()
                .HasKey(cp => new { cp.ConversationId, cp.PersonId });

            modelBuilder.Entity<Message>(m =>
            {
                m.Property(x => x.Kind).HasConversion<string>();
                m.HasIndex(x => new { x.ConversationId, x.Timestamp, x.Seq });
                m.HasIndex(x => new { x.SenderId, x.Timestamp });
                m.HasIndex(x => x.EmailId);
            });

            modelBuilder.Entity<ImportFile>(f =>
            {
                f.Property(x => x.Kind).HasConversion<string>();
                f.HasIndex(x => x.Sha256).IsUnique();
            });

            modelBuilder.Entity<MessageSource>(s =>
            {
                s.HasIndex(x => x.MessageId);
                s.HasIndex(x => new { x.ImportFileId, x.LineNumber });
            });

            modelBuilder.Entity<Attachment>(a =>
            {
                a.Property(x => x.Kind).HasConversion<string>();
                a.HasIndex(x => x.Sha256).IsUnique();
                a.HasOne(x => x.Data)
                    .WithOne()
                    .HasForeignKey<AttachmentData>(d => d.AttachmentId);
            });

            modelBuilder.Entity<AttachmentData>()
                .HasKey(d => d.AttachmentId);
        }
    }
}
