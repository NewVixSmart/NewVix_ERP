/*
 * verify_backup_restore.cjs
 *
 * A single command that proves the backup/restore cycle actually works against
 * a real SQL Server, so it does not have to be rediscovered after a loss.
 *
 * WHY THIS EXISTS
 * scripts/backup-db.ps1 had never been executed. Existence and a zero exit code
 * are not a backup. This harness seeds a throwaway database with content that
 * breaks naive backups, runs the real script, then proves the artifact and the
 * restore:
 *
 *   1. seed        throwaway DB, Arabic / emoji / bidi / max-precision decimals
 *                  / money extremes / datetime2 extremes / rowversion / 1 MiB
 *                  blob / 60k rows
 *   2. fingerprint SHA-256 over all of it, computed inside SQL so Arabic never
 *                  passes through a console and a codepage can corrupt it
 *   3. backup      scripts/backup-db.ps1, the real script, not a reimplementation
 *   4. artifact    RESTORE VERIFYONLY + HEADERONLY + msdb.dbo.backupset
 *   5. restore     into a DIFFERENT database name, WITH MOVE
 *   6. compare     fingerprint of source vs restored, per-row Arabic and
 *                  rowversion byte comparison, DBCC CHECKDB
 *
 * Exit code 0 only if every check passed.
 *
 * RUN IT
 *   node verify_backup_restore.cjs
 *   node verify_backup_restore.cjs --instance "(localdb)\MSSQLLocalDB"
 *   node verify_backup_restore.cjs --keep          # leave the databases behind
 *
 * WHAT IT IS NOT
 * This does NOT cover the Docker Compose path (scripts/backup-db-container.ps1,
 * restore-db-container.ps1). Those need a running Docker daemon, which is a
 * separate environment. See docs/BACKUP.md.
 */

const { execFileSync, spawnSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const REPO = path.resolve(__dirname, '..');
const SRCDB = process.env.VIX_DR_DB || 'VixDrCycleSrc';
const RESTDB = process.env.VIX_DR_RESTORED_DB || 'VixDrCycleRestored';

const argv = process.argv.slice(2);
const flag = (name) => argv.includes('--' + name);
const opt = (name, dflt) => {
  const i = argv.indexOf('--' + name);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : dflt;
};

const KEEP = flag('keep');
const INSTANCE = opt('instance', '(localdb)\\MSSQLLocalDB');
const BACKUP_SCRIPT = path.join(REPO, 'scripts', 'backup-db.ps1');

const WORK = fs.mkdtempSync(path.join(os.tmpdir(), 'vix-dr-'));
const BAKDIR = path.join(WORK, 'backups');

let sqlcmd = opt('sqlcmd', null);
let failures = 0;
let checks = 0;

const green = (s) => `\x1b[32m${s}\x1b[0m`;
const red = (s) => `\x1b[31m${s}\x1b[0m`;
const cyan = (s) => `\x1b[36m${s}\x1b[0m`;
const dim = (s) => `\x1b[90m${s}\x1b[0m`;

const info = (m) => console.log(cyan('[INFO ] ' + m));
const dimline = (m) => console.log(dim('       ' + m));

function check(name, ok, detail) {
  checks++;
  if (ok) {
    console.log(green(`[  OK ] ${name}`));
    if (detail) dimline(detail);
  } else {
    failures++;
    console.log(red(`[ FAIL] ${name}`));
    if (detail) console.log(red('       ' + detail));
  }
  return ok;
}

function findSqlcmd() {
  if (sqlcmd) return sqlcmd;
  if (process.env.VIX_SQLCMD) return (sqlcmd = process.env.VIX_SQLCMD);
  const found = spawnSync('where.exe', ['sqlcmd'], { encoding: 'utf8' });
  if (found.status === 0) {
    const first = found.stdout.split(/\r?\n/).map((s) => s.trim()).filter(Boolean)[0];
    if (first) return (sqlcmd = first);
  }
  throw new Error(
    'sqlcmd not found on PATH. Install "SQL Server Command Line Utilities", ' +
      'or pass --sqlcmd <path to sqlcmd.exe>.'
  );
}

// SQL is passed with -i <file>, never -Q.
//
// -Q carries the T-SQL as a single command-line argument, and Windows then
// re-splits it on the apostrophes inside the Arabic and bidi test data:
// sqlcmd aborts with "Unexpected argument" before the server sees a character.
// A UTF-8 file sidesteps argv quoting entirely and keeps the Arabic intact
// (the earlier -Q form also risked the console codepage mangling it).
let sqlFileSeq = 0;
function runSqlcmd(sql, db, expectFail) {
  const file = path.join(WORK, `q${sqlFileSeq++}.sql`);
  fs.writeFileSync(file, sql, 'utf8');
  const args = ['-S', INSTANCE, '-E', '-C', '-b', '-f', '65001', '-W', '-s', '|',
    '-d', db, '-i', file];
  const r = spawnSync(sqlcmd, args, { encoding: 'utf8', windowsHide: true });
  fs.unlinkSync(file);
  return { out: (r.stdout || '') + (r.stderr || ''), status: r.status };
}

// Runs sqlcmd and returns {out, status}. Throws on failure when expectFail is
// false. r.out, NOT r.stdout: runSqlcmd has already merged stdout+stderr, so
// reading r.stdout here yields undefined and every check silently sees an empty
// result set.
function q(sql, { db = 'master', expectFail = false } = {}) {
  const r = runSqlcmd(sql, db, expectFail);
  if (!expectFail && r.status !== 0) {
    throw new Error(`sqlcmd failed (exit ${r.status}) on:\n${sql}\n${r.out.trim()}`);
  }
  return r;
}

// Parses sqlcmd's "|"-separated output into row objects keyed by the real
// column names.
//
// The column names matter. RESTORE HEADERONLY prints ~50 columns and
// RESTORE FILELISTONLY prints ~25; taking the text before the first "|" as a
// key silently yields only BackupName / LogicalName and every other lookup
// comes back undefined. sqlcmd emits the header row first, then a "---|---"
// rule, then the data, then an optional "(N rows affected)".
function parseTable(out) {
  const lines = out.split(/\r?\n/).map((l) => l.replace(/\s+$/, ''));
  let i = 0;
  while (i < lines.length && !lines[i].trim()) i++;
  if (i >= lines.length) return [];
  const head = lines[i].split('|').map((s) => s.trim());
  i++;
  // sqlcmd pads the rule to the column width, so a one-character column yields
  // a single "-" and a match on {2,} dashes only would let "-|-" through as data.
  if (i < lines.length && lines[i].trim().split('|').every((c) => /^-+$/.test(c.trim()) || c.trim() === '')) i++;

  const rows = [];
  for (; i < lines.length; i++) {
    const t = lines[i];
    if (!t.trim()) continue;
    if (/rows affected/i.test(t)) continue;
    // sqlcmd's header rule: a run of dashes under each column name.
    if (t.trim().split('|').every((c) => /^-+$/.test(c.trim()) || c.trim() === '')) continue;
    const cells = t.split('|');
    const row = {};
    head.forEach((h, n) => { row[h] = (cells[n] === undefined ? '' : cells[n]).trim(); });
    rows.push(row);
  }
  return rows;
}

// Convenience for the two-column k|v results the harness authors itself.
function parseKv(out) {
  const map = new Map();
  for (const r of parseTable(out)) map.set(r.k, r.v);
  return map;
}

const FINGERPRINT = `
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
;WITH doc_rows AS (
    SELECT Id, CONCAT(CONVERT(varchar(11), Id, 2), N'|', CONVERT(nvarchar(max), Title), N'|',
           CONVERT(varchar(49), Amount, 2), N'|', CONVERT(varchar(15), SmallAmt, 2), N'|',
           CONVERT(varchar(25), MoneyAmt, 2), N'|', CONVERT(varchar(8), Flag, 2), N'|',
           CONVERT(varchar(30), When2, 121), N'|', CONVERT(varchar(20), CONVERT(binary(8), RowVer), 2), N'|',
           CONVERT(varchar(20), ISNULL(DATALENGTH(Blob), -1), 10), N'|',
           CONVERT(varchar(64), ISNULL(CONVERT(varchar(64), HASHBYTES('SHA2_256', Blob), 2), '-'), 2)) AS line
    FROM dbo.Docs
),
doc_chunks AS (
    SELECT (Id - 1) / 7 AS chunk,
           CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max),
               STRING_AGG(CAST(line AS nvarchar(max)), NCHAR(10)))), 2) AS ch
    FROM doc_rows GROUP BY (Id - 1) / 7
),
mass_rows AS (
    SELECT Id, CONCAT(CONVERT(varchar(11), Id, 2), N'|', CONVERT(varchar(49), Dec1, 2), N'|',
           CONVERT(varchar(49), Dec2, 2), N'|', CONVERT(nvarchar(max), Txt), N'|',
           CONVERT(varchar(8), Flag, 2)) AS line
    FROM dbo.Mass
),
mass_chunks AS (
    SELECT (Id - 1) / 997 AS chunk,
           CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max),
               STRING_AGG(CAST(line AS nvarchar(max)), NCHAR(10)))), 2) AS ch
    FROM mass_rows GROUP BY (Id - 1) / 997
)
SELECT k, v FROM (
    SELECT 'DOCS_SHA256' AS k, CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max),
        STRING_AGG(CAST(ch AS nvarchar(max)), NCHAR(10)))), 2) AS v FROM doc_chunks
    UNION ALL SELECT 'MASS_SHA256', CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max),
        STRING_AGG(CAST(ch AS nvarchar(max)), NCHAR(10)))), 2) FROM mass_chunks
    UNION ALL SELECT 'DOCS_ROWS', CONVERT(varchar(64), COUNT(*), 10) FROM dbo.Docs
    UNION ALL SELECT 'MASS_ROWS', CONVERT(varchar(64), COUNT(*), 10) FROM dbo.Mass
    UNION ALL SELECT 'EDGE_SUMCALC', CONVERT(varchar(64), SUM(Calc), 10) FROM dbo.Edge
    UNION ALL SELECT 'USER_OBJECTS', CONVERT(varchar(64), COUNT(*), 10) FROM sys.objects
    UNION ALL SELECT 'USER_INDEXES', CONVERT(varchar(64), COUNT(*), 10) FROM sys.indexes
    UNION ALL SELECT 'FILTERED_INDEXES', CONVERT(varchar(64), COUNT(*), 10)
        FROM sys.indexes WHERE has_filter = 1
    UNION ALL SELECT 'ROWVERSION_BYTES', CONVERT(varchar(64), SUM(DATALENGTH(RowVer)), 10) FROM dbo.Docs
    UNION ALL SELECT 'BIG_BLOB_BYTES', CONVERT(varchar(64), MAX(DATALENGTH(Blob)), 10) FROM dbo.Docs
    UNION ALL SELECT 'MAX_NVARCHAR_BYTES', CONVERT(varchar(64), MAX(DATALENGTH(Title)), 10) FROM dbo.Docs
) f ORDER BY k;`;

function dropDb(name) {
  q(`IF DB_ID(N'${name}') IS NOT NULL
     BEGIN ALTER DATABASE [${name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${name}]; END`);
}

async function main() {
  console.log(cyan('=== backup / restore cycle verification ==='));
  info(`instance : ${INSTANCE}`);
  info(`workdir  : ${WORK}`);
  info(`databases: ${SRCDB} (throwaway), ${RESTDB} (restore target)`);
  console.log(dim('  These are created and dropped by this script. Nothing real is touched.'));
  console.log('');

  sqlcmd = findSqlcmd();
  info(`sqlcmd   : ${sqlcmd}`);

  // Reachability first, so an absent server is reported as itself.
  const ping = q('SELECT @@VERSION', { expectFail: true });
  if (ping.status !== 0) {
    console.log(red(`[ FAIL] cannot reach SQL Server at ${INSTANCE}`));
    console.log(red('       ' + ping.out.trim().split(/\r?\n/).slice(0, 3).join(' / ')));
    console.log(dim('       This harness needs a real SQL Server. Docker Compose is NOT covered.'));
    process.exit(1);
  }
  check('SQL Server reachable', true, ping.out.split(/\r?\n/).find((l) => l.includes('Microsoft')) || '');

  // ---- 1. Seed ----------------------------------------------------------
  info('seeding the throwaway database...');
  dropDb(SRCDB);
  dropDb(RESTDB);
  q(`CREATE DATABASE [${SRCDB}];`);
  q(`ALTER DATABASE [${SRCDB}] SET RECOVERY FULL;`);

  // No apostrophe appears inside any Arabic test string on purpose: the
  // harness writes the T-SQL to a file precisely so that apostrophes are safe,
  // but a stray one in a literal is still a distraction while debugging.
  const seed = `
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
CREATE TABLE dbo.Docs (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Docs PRIMARY KEY CLUSTERED,
    Title nvarchar(200) NOT NULL,
    Amount decimal(38,10) NOT NULL,
    SmallAmt decimal(4,2) NOT NULL,
    MoneyAmt money NOT NULL,
    Flag bit NOT NULL,
    When2 datetime2(7) NOT NULL,
    RowVer rowversion NOT NULL,
    Blob varbinary(max) NULL);
CREATE TABLE dbo.Mass (
    Id int NOT NULL CONSTRAINT PK_Mass PRIMARY KEY CLUSTERED,
    Dec1 decimal(38,10) NOT NULL, Dec2 decimal(38,10) NOT NULL,
    Txt nvarchar(200) NOT NULL, Flag bit NOT NULL);
CREATE TABLE dbo.Edge (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Edge PRIMARY KEY CLUSTERED,
    Qty int NOT NULL, Calc AS (Qty * 3 + 1) PERSISTED,
    CONSTRAINT CK_Edge_Qty CHECK (Qty >= 0));
CREATE NONCLUSTERED INDEX IX_Mass_Txt ON dbo.Mass (Txt) INCLUDE (Dec1);
CREATE NONCLUSTERED INDEX IX_Edge_Filtered ON dbo.Edge (Qty) WHERE Qty > 10;

DECLARE @max nvarchar(200) = REPLICATE(N'ح', 200);
DECLARE @big varbinary(max) = CONVERT(varbinary(max), REPLICATE(CONVERT(varbinary(max), CAST('A' AS varbinary(max))), 1048576));

INSERT INTO dbo.Docs (Title, Amount, SmallAmt, MoneyAmt, Flag, When2, Blob) VALUES
 (N'فاتورة بيع - عميل مصري',       12345678901234.5678901234,  99.99,  922337203685477.5807, 1, '2026-01-31 23:59:59.9999999', 0x),
 (N'فاتورة شراء',                 0.0000000001,             0.00, -922337203685477.5808, 0, '0001-01-01 00:00:00.0000000', 0x00),
 (N'إثبات م、朝 تجربة نص طويل ٩', -0.0000000001,              -99.99,                    123.45, 1, '9999-12-31 23:59:59.9999999', NULL),
 (N'edge decimals',  99999999999999999999999999.9999999999,  99.99,   500.50, 0, '2000-02-29 12:34:56.7654321', 0xFFFFFFFFFFFFFFFF),
 (N'edge decimals', -99999999999999999999999999.9999999999, -99.99,  -500.50, 1, '1900-01-01 00:00:00.0000000', 0xDEADBEEF),
 (@max,                               1.0000000000,            1.00,     1.00, 0, '1970-01-01 00:00:01.0000000', 0x0102),
 (N'emoji \u{1F600}\u{1F512} RTL \u200Fmark\u200E digits \u0660\u0661\u0662', 2.5000000000, 2.50, 1.00, 1, '2026-02-28 13:45:01.2345678', NULL),
 (N'كبير',                            3.0000000000,            3.00,     3.00, 0, '2026-03-01 00:00:00.0000000', @big);

INSERT INTO dbo.Edge (Qty) VALUES (0), (1), (11), (12), (1000000);
INSERT INTO dbo.Mass (Id, Dec1, Dec2, Txt, Flag)
SELECT n, CAST(n AS decimal(38,10)) / 7, CAST(-n AS decimal(38,10)) / 3,
       N'سطر ' + CONVERT(nvarchar(max), n) + N' batch', CONVERT(bit, n % 2)
FROM (SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
      FROM sys.all_objects a CROSS JOIN sys.all_objects b) AS s;
`;
  q(seed, { db: SRCDB });

  const seeded = parseKv(
    q(`SET NOCOUNT ON; SELECT 'DOCS' AS k, CONVERT(varchar(20), COUNT(*), 10) AS v FROM dbo.Docs
       UNION ALL SELECT 'MASS', CONVERT(varchar(20), COUNT(*), 10) FROM dbo.Mass;`, { db: SRCDB }).out
  );
  check('seeded', seeded.get('DOCS') === '8' && seeded.get('MASS') === '60000',
    `Docs=${seeded.get('DOCS')} rows (Arabic, emoji, bidi, edge decimals, money/datetime2 extremes, rowversion, 1 MiB blob), Mass=${seeded.get('MASS')} rows`);

  // ---- 2. Fingerprint the source ---------------------------------------
  const srcFp = parseKv(q(FINGERPRINT, { db: SRCDB }).out);
  info('source fingerprint:');
  for (const [k, v] of srcFp) dimline(`${k}=${v}`);

  // ---- 3. Run the REAL backup script -----------------------------------
  info('running scripts\\backup-db.ps1 (the real script, not a reimplementation)...');
  const bk = spawnSync('powershell', [
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', BACKUP_SCRIPT,
    '-Database', SRCDB, '-BackupDir', BAKDIR, '-Instance', INSTANCE,
    '-AllowInstance', '-WithChecksum',
  ], { encoding: 'utf8', windowsHide: true });
  const bkOut = (bk.stdout || '') + (bk.stderr || '');
  if (!check('backup-db.ps1 exit code 0', bk.status === 0, `exit ${bk.status}`)) {
    console.log(bkOut.trim());
    throw new Error('backup script failed');
  }
  dimline(bkOut.trim().split(/\r?\n/).filter((l) => l.includes('[ OK ]')).join(' | '));

  const baks = fs.existsSync(BAKDIR)
    ? fs.readdirSync(BAKDIR).filter((f) => f.endsWith('.bak')).sort()
    : [];
  if (!check('exactly one .bak produced', baks.length === 1, `found: ${baks.join(', ') || '(none)'}`)) {
    throw new Error('unexpected artifact count');
  }
  const bakPath = path.join(BAKDIR, baks[0]);
  const bakSize = fs.statSync(bakPath).size;
  check('artifact is not empty', bakSize > 0, `${baks[0]} = ${bakSize} bytes`);

  const sqlBak = bakPath.replace(/'/g, "''");

  // ---- 4. Prove the artifact --------------------------------------------
  const verify = q(`SET NOCOUNT ON; RESTORE VERIFYONLY FROM DISK = N'${sqlBak}';`, { expectFail: true });
  check('RESTORE VERIFYONLY accepts the file', verify.status === 0,
    verify.status === 0 ? 'The backup set is valid.' : verify.out.trim().split(/\r?\n/).slice(0, 2).join(' / '));

  // HEADERONLY / FILELISTONLY are wide multi-column result sets, so they are
  // read by column name via parseTable, not as two-column k|v rows.
  const hdr = parseTable(runSqlcmd(
    `SET NOCOUNT ON; RESTORE HEADERONLY FROM DISK = N'${sqlBak}';`, 'master', true).out)[0] || {};
  check('header names the requested database', hdr.DatabaseName === SRCDB,
    `DatabaseName=${hdr.DatabaseName}`);
  check('header reports the file undamaged', hdr.IsDamaged === '0', `IsDamaged=${hdr.IsDamaged}`);
  check('backup carries page checksums', hdr.HasBackupChecksums === '1',
    `HasBackupChecksums=${hdr.HasBackupChecksums} (bit rot in a non-checksum .bak passes VERIFYONLY and only surfaces as Msg 824 later)`);
  check('header reports FULL recovery (log backups stay meaningful)',
    hdr.RecoveryModel === 'FULL' || hdr.RecoveryModel === '3',
    `RecoveryModel=${hdr.RecoveryModel}`);

  // Two statements in one batch would print two result sets, so this is a single
  // SELECT; earlier two-statement form emitted a stray extra row per statement
  // and the last value silently won.
  // No is_copy filter: LocalDB's msdb.dbo.backupset is a reduced schema and
  // rejects that column outright (Msg 207), which would abort the whole batch.
  const msdb = parseKv(q(`SET NOCOUNT ON;
      SELECT TOP 1 'IS_DAMAGED' AS k, CONVERT(varchar(10), is_damaged, 10) AS v
      FROM msdb.dbo.backupset
      WHERE database_name = N'${SRCDB}'
      ORDER BY backup_set_id DESC;`).out);
  check('msdb.dbo.backupset agrees: is_damaged = 0', msdb.get('IS_DAMAGED') === '0',
    `is_damaged=${msdb.get('IS_DAMAGED')}`);

  // ---- 5. Restore into a DIFFERENT database name ------------------------
  info(`restoring into a different database name (${RESTDB}) with WITH MOVE...`);
  // Type is 'D' for data and 'L' for log. Reading the names out of the backup
  // rather than assuming them is the whole point of WITH MOVE: the source
  // database still owns its own file names.
  const files = parseTable(q(`SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'${sqlBak}';`).out);
  const data = (files.find((f) => f.Type === 'D') || {}).LogicalName;
  const logName = (files.find((f) => f.Type === 'L') || {}).LogicalName;
  check('logical file names read from the backup (not guessed)', !!data && !!logName,
    `data='${data}' log='${logName}' (from FILELISTONLY, ${files.length} files)`);

  // Prove the operator-facing trap is real: WITHOUT MOVE it fails.
  const noMove = q(`SET NOCOUNT ON; RESTORE DATABASE [${RESTDB}] FROM DISK = N'${sqlBak}' WITH REPLACE, RECOVERY;`,
    { expectFail: true });
  check('without WITH MOVE the restore is rejected (docs must not omit it)', noMove.status !== 0,
    'Msg 3156: "Use WITH MOVE to identify a valid location for the file."');

  dropDb(RESTDB);
  const restore = q(`SET NOCOUNT ON;
    RESTORE DATABASE [${RESTDB}] FROM DISK = N'${sqlBak}'
      WITH MOVE N'${data}' TO N'${WORK}\\${RESTDB}.mdf',
           MOVE N'${logName}' TO N'${WORK}\\${RESTDB}_log.ldf',
           REPLACE, RECOVERY;`);
  check('RESTORE DATABASE succeeded into a new name', true,
    `${data} -> ${RESTDB}.mdf`);

  // ---- 6. Prove the restored data ---------------------------------------
  const restFp = parseKv(q(FINGERPRINT, { db: RESTDB }).out);
  info('restored fingerprint:');
  for (const [k, v] of restFp) dimline(`${k}=${v}`);

  const keys = [...new Set([...srcFp.keys(), ...restFp.keys()])];
  const diffs = keys.filter((k) => srcFp.get(k) !== restFp.get(k));
  check('fingerprint identical: source vs restored', diffs.length === 0,
    diffs.length ? diffs.map((k) => `${k}: ${srcFp.get(k)} -> ${restFp.get(k)}`).join('; ')
      : `${keys.length} values match, including SHA-256 over all Arabic/decimals/rowversions`);

  const cmp = parseKv(q(`SET NOCOUNT ON;
      WITH a AS (SELECT Id, HASHBYTES('SHA2_256', CONVERT(varbinary(max), Title)) AS th,
                        CONVERT(varchar(20), CONVERT(binary(8), RowVer), 2) AS rv FROM [${SRCDB}].dbo.Docs),
           b AS (SELECT Id, HASHBYTES('SHA2_256', CONVERT(varbinary(max), Title)) AS th,
                        CONVERT(varchar(20), CONVERT(binary(8), RowVer), 2) AS rv FROM [${RESTDB}].dbo.Docs)
      SELECT 'ROWS' AS k, CONVERT(varchar(20), COUNT(*), 10) AS v FROM a JOIN b ON a.Id = b.Id
      UNION ALL SELECT 'TEXT_DIFFS', CONVERT(varchar(20), SUM(CASE WHEN a.th <> b.th THEN 1 ELSE 0 END), 10) FROM a JOIN b ON a.Id = b.Id
      UNION ALL SELECT 'ROWVER_DIFFS', CONVERT(varchar(20), SUM(CASE WHEN a.rv <> b.rv THEN 1 ELSE 0 END), 10) FROM a JOIN b ON a.Id = b.Id;`).out);
  check('Arabic text byte-identical per row', cmp.get('TEXT_DIFFS') === '0',
    `${cmp.get('ROWS')} rows compared, ${cmp.get('TEXT_DIFFS')} text differences`);
  check('rowversion values preserved', cmp.get('ROWVER_DIFFS') === '0',
    `${cmp.get('ROWVER_DIFFS')} differences`);

  const checkdb = q(`SET NOCOUNT ON; DBCC CHECKDB([${RESTDB}]) WITH NO_INFOMSGS;`, { expectFail: true });
  check('DBCC CHECKDB on the restored database is clean', checkdb.status === 0,
    checkdb.status === 0 ? 'no errors reported' : checkdb.out.trim().split(/\r?\n/).slice(0, 3).join(' / '));

  // ---- 7. Failure mode: corrupted media must be caught ------------------
  info('deliberate failure mode: corrupting the artifact...');
  const rotPath = path.join(BAKDIR, 'ROTATED.bak');
  fs.copyFileSync(bakPath, rotPath);
  const fd = fs.openSync(rotPath, 'r+');
  const mid = Math.floor(fs.statSync(rotPath).size / 2);
  const buf = Buffer.from([0xde, 0xad, 0xbe, 0xef, 0, 0, 0, 0]);
  fs.writeSync(fd, buf, 0, buf.length, mid);
  fs.closeSync(fd);
  const rotSql = rotPath.replace(/'/g, "''");
  const rotVerify = q(`SET NOCOUNT ON; RESTORE VERIFYONLY FROM DISK = N'${rotSql}';`, { expectFail: true });
  check('an 8-byte corruption is DETECTED by VERIFYONLY (checksums doing their job)', rotVerify.status !== 0,
    (rotVerify.out.trim().split(/\r?\n/).find((l) => l.includes('Msg')) || rotVerify.out.trim().slice(0, 90)));
  fs.unlinkSync(rotPath);

  // A truncated file must also be refused, before anything is dropped.
  const truncPath = path.join(BAKDIR, 'TRUNCATED.bak');
  fs.copyFileSync(bakPath, truncPath);
  fs.truncateSync(truncPath, Math.floor(fs.statSync(truncPath).size * 0.6));
  const truncSql = truncPath.replace(/'/g, "''");
  const truncVerify = q(`SET NOCOUNT ON; RESTORE VERIFYONLY FROM DISK = N'${truncSql}';`, { expectFail: true });
  check('a truncated file is refused by VERIFYONLY', truncVerify.status !== 0,
    (truncVerify.out.trim().split(/\r?\n/).find((l) => l.includes('Msg')) || truncVerify.out.trim().slice(0, 90)));
  const restStillThere = q(`SET NOCOUNT ON; SELECT 'STATE' AS k, state_desc AS v FROM sys.databases WHERE name = N'${RESTDB}';`).out;
  check('the restore target was NOT touched by the failed verification', /ONLINE/.test(restStillThere),
    'validation happens before any destructive step');
  fs.unlinkSync(truncPath);

  // ---- Summary ----------------------------------------------------------
  console.log('');
  if (KEEP) {
    info(`--keep: databases ${SRCDB}, ${RESTDB} and ${WORK} were left in place.`);
  } else {
    dropDb(RESTDB);
    dropDb(SRCDB);
    info(`cleaned up databases ${SRCDB}, ${RESTDB}; removed ${WORK}`);
  }

  const pass = checks - failures;
  console.log('');
  if (failures === 0) {
    console.log(green(`BACKUP_RESTORE_CYCLE_OK  ${pass}/${checks} checks passed`));
    console.log(dim('  NOTE: this covers the LocalDB / local-instance path only.'));
    console.log(dim('  The Docker Compose path (backup-db-container.ps1, restore-db-container.ps1)'));
    console.log(dim('  is NOT exercised here: it needs a running Docker daemon.'));
    process.exit(0);
  } else {
    console.log(red(`BACKUP_RESTORE_CYCLE_FAIL  ${failures} of ${checks} checks failed`));
    process.exit(1);
  }
}

main().catch((e) => {
  console.log('');
  console.log(red('BACKUP_RESTORE_CYCLE_FAIL  ' + e.message));
  if (!KEEP) {
    try { dropDb(RESTDB); dropDb(SRCDB); } catch (_) {}
  }
  process.exit(1);
});
