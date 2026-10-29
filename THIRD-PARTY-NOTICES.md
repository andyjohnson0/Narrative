# Third-party notices

Narrative uses the open-source packages below. None of their code is in this repository: NuGet
downloads them when you build. If you distribute a build of Narrative, the packages' licences
apply to those copies, so include this file and the licence texts linked below.

## Packages used by the application

| Package | Version | Licence | Copyright |
|---|---|---|---|
| [MailKit](https://github.com/jstedfast/MailKit) and [MimeKit](https://github.com/jstedfast/MimeKit) | 4.18.2 | MIT | .NET Foundation and Contributors |
| [BouncyCastle.Cryptography](https://github.com/bcgit/bc-csharp) (a MimeKit dependency) | 2.7.0 | MIT | Copyright © Legion of the Bouncy Castle Inc. 2000-2026 |
| [System.Security.Cryptography.Pkcs](https://github.com/dotnet/runtime) (a MimeKit dependency) | 10.0.13 | MIT | © Microsoft Corporation |
| [Scriban](https://github.com/scriban/scriban) | 7.5.0 | BSD-2-Clause | Copyright (c) 2016-2026, Alexandre Mutel |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | 2.0.13 | MIT | © Microsoft Corporation |
| [Microsoft.EntityFrameworkCore.Sqlite](https://github.com/dotnet/efcore) and its Microsoft.EntityFrameworkCore.\*, Microsoft.Data.Sqlite.Core and Microsoft.Extensions.\* dependencies | 10.0.13 | MIT | © Microsoft Corporation |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) (core, provider.e_sqlite3, lib.e_sqlite3, bundle_e_sqlite3) | 2.1.13 | Apache-2.0 | Copyright 2014-2024 SourceGear, LLC |
| [SQLite](https://sqlite.org/) (the native library inside SQLitePCLRaw.lib.e_sqlite3) | | Public domain | |

## Packages used only by the tests

These aren't part of the application.

| Package | Version | Licence | Copyright |
|---|---|---|---|
| [xunit](https://github.com/xunit/xunit) | 2.9.3 | Apache-2.0 | Copyright (C) .NET Foundation |
| [xunit.runner.visualstudio](https://github.com/xunit/visualstudio.xunit) | 3.1.4 | Apache-2.0 | Copyright (C) .NET Foundation |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | 17.14.1 | MIT | © Microsoft Corporation |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) | 6.0.4 | MIT | Coverlet contributors |

## Licence texts

### MIT

The MIT licence text is in [LICENSE.md](LICENSE.md) and at <https://opensource.org/license/mit>.
Each MIT package above is covered by it under its own copyright line.

### Apache License 2.0

<https://www.apache.org/licenses/LICENSE-2.0>

### BSD 2-Clause (Scriban)

```
Copyright (c) 2016-2026, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

The versions listed are those in the project files at the time of writing. Check them again when
you upgrade a package.
