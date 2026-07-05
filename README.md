# AutoDev Runner

Công cụ tự động phát triển phần mềm bằng AI, chạy trên Windows. Nó định kỳ gọi
**Codex CLI** và **Claude CLI** (đã đăng nhập sẵn trên máy) để đọc một project
mục tiêu, tự lập kế hoạch, chọn task có giá trị nhất, triển khai, chạy kiểm thử,
commit lên một nhánh an toàn, và gửi email tóm tắt cho bạn — tất cả mà không cần
duyệt từng task.

Ứng dụng là một app .NET 8 duy nhất, bao gồm:

- **lập lịch qua Windows Task Scheduler** (gọi `AutoDevRunner.exe --run-due` mỗi N giờ),
- một **Web API local** (`localhost`, không cần đăng nhập trong bản MVP),
- một **dashboard web local**,
- **adapter provider** cho Codex (chính) và Claude,
- lưu trữ bằng **PostgreSQL** cho project, lịch sử run và trạng thái provider,
- **resume** từ trạng thái/tóm tắt của lần chạy trước,
- **báo cáo email** sau mỗi lần chạy qua **EmailJS**,
- và có thể chạy như một **app console hoặc một Windows Service**.

---

## Yêu cầu

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **PostgreSQL** đang chạy ở local (hoặc bất kỳ đâu truy cập được). Connection
  string mặc định trỏ tới `localhost:5432`, database `autodev`, user `postgres`.
  App tự tạo database + bảng ở lần khởi động đầu tiên (`EnsureCreated`), nên user
  được cấu hình cần quyền `CREATEDB` (superuser mặc định đã có sẵn).
- **Codex CLI** và/hoặc **Claude CLI** đã cài đặt và **đã đăng nhập sẵn** trên
  máy, có thể gọi được qua `PATH` (kiểm tra bằng `codex --version` / `claude --version`).
- `git` trong `PATH` (dùng cho branch/commit/push).
- (Tùy chọn) một tài khoản [EmailJS](https://www.emailjs.com/) để nhận email báo cáo.

> Runner không tự đăng nhập thay bạn. Nó gọi các CLI dưới danh nghĩa user hiện
> tại và dựa vào phiên đăng nhập sẵn có của chúng.

---

## Bắt đầu nhanh (console)

```powershell
# từ thư mục gốc của repo
dotnet run --project src/AutoDevRunner
```

Sau đó mở dashboard: **http://localhost:5099**

1. Vào **Projects → + New project**.
2. Đặt **Name** và **Repo path** (đường dẫn tuyệt đối tới một git repo).
3. (Tùy chọn) Đặt **Brief file** — mặc định là `ai-autonomous.md` trong repo.
   Xem mẫu tại [`docs/ai-autonomous.example.md`](docs/ai-autonomous.example.md).
4. (Tùy chọn) Đặt **Validation command** như `dotnet build` hoặc `npm test`.
5. Bấm **Run** để chạy ngay, hoặc để Task Scheduler tự kích hoạt.

Lần khởi động đầu tiên sẽ tự tạo schema PostgreSQL. Mỗi lần chạy sẽ ghi file tóm
tắt `<repo>/.ai-runner/runs/*.md` vào trong repo mục tiêu.

### Chạy tất cả project đến hạn một lần (cách Task Scheduler gọi)

```powershell
dotnet run --project src/AutoDevRunner -- --run-due
```

Lệnh này chạy mọi project đang bật và không bị tạm dừng, theo thứ tự ưu tiên, rồi
thoát — không khởi động web host.

---

## Cấu hình (`src/AutoDevRunner/appsettings.json`)

```jsonc
"ConnectionStrings": {
  "Postgres": "Host=localhost;Port=5432;Database=autodev;Username=postgres;Password=postgres"
},
"AutoDev": {
  "Scheduler": {
    "Enabled": false,          // scheduler nội bộ TẮT; ưu tiên dùng Task Scheduler
    "IntervalHours": 5,        // chỉ dùng khi Enabled = true
    "RunOnStartup": false
  },
  "Providers": {
    "Codex":  { "Enabled": true, "Command": "codex",  "Arguments": "exec --skip-git-repo-check \"{PROMPT}\"" },
    "Claude": { "Enabled": true, "Command": "claude", "Arguments": "-p \"{PROMPT}\" --permission-mode acceptEdits" }
  },
  "Email": {                    // EmailJS — bật Enabled + điền thông tin để nhận báo cáo
    "Enabled": false,
    "ApiUrl": "https://api.emailjs.com/api/v1.0/email/send",
    "ServiceId": "",           // EmailJS service id
    "TemplateId": "",          // EmailJS template id
    "PublicKey": "",           // EmailJS public key  (gửi dưới dạng user_id)
    "PrivateKey": "",          // EmailJS private key (gửi dưới dạng accessToken)
    "ToEmail": "you@example.com"
  },
  "Planner": {                  // Creative planner OpenAI (tùy chọn) chạy TRƯỚC mỗi lần gọi CLI
    "Enabled": true,
    "Model": "gpt-4.1",
    "ApiKey": "",              // OpenAI API key (để đây, KHÔNG cần env var toàn cục); trống → fallback env OPENAI_API_KEY
    "ApiUrl": "https://api.openai.com/v1/responses",
    "VectorStoreIds": [        // knowledge base gắn qua file_search; đổi id ở đây để thay KB
      "vs_6a49ceb5fce081919de8048b6a6d548c"
    ]
  }
}
```

### Lưu trữ (PostgreSQL)

Đặt `ConnectionStrings:Postgres`. Ở lần khởi động đầu, app chạy `EnsureCreated` để
tạo database `autodev` và toàn bộ bảng. Muốn dùng Postgres managed/từ xa thì chỉ
việc trỏ connection string vào đó. Cả web host và tiến trình `--run-due` kết nối
độc lập với nhau (Postgres xử lý truy cập đồng thời).

### Email (EmailJS)

Báo cáo được gửi bằng cách POST tới EmailJS REST API — không cần SMTP server.

1. Tạo một EmailJS service + một email template.
2. Trong template, dùng các biến: `{{subject}}`, `{{message}}`, `{{to_email}}`,
   `{{project}}`, `{{status}}`. Đặt trường "To email" của template là `{{to_email}}`.
3. Trong tài khoản EmailJS, bật **"Allow EmailJS API for non-browser
   applications"** và sao chép **public key** lẫn **private key**.
4. Điền `ServiceId`, `TemplateId`, `PublicKey`, `PrivateKey`, `ToEmail` và đặt
   `Enabled: true`.

### Creative planner + knowledge base (OpenAI)

Trước mỗi lần chạy, nếu `AutoDev:Planner:Enabled = true`, runner gọi **một lần**
OpenAI Responses API kèm tool `file_search` gắn vào (các) vector store trong
`VectorStoreIds`. Kết quả là một **bản plan sáng tạo** bám theo knowledge base,
được chèn vào prompt gửi cho Codex/Claude CLI. Prompt được thiết kế cho AI **toàn
quyền sáng tạo, không cần approve** — accountability duy nhất là báo cáo/email sau
mỗi lần chạy.

- **Đổi knowledge base:** sửa `VectorStoreIds` trong `appsettings.json` (nhiều id
  được). Để trống mảng → planner vẫn chạy nhưng không dùng file_search.
- **API key:** điền `ApiKey` ngay trong `appsettings.json` (khỏi cần env var toàn
  cục — tránh ảnh hưởng auth của Codex CLI). Nếu trống, runner mới đọc biến môi
  trường `OPENAI_API_KEY`.
- **Fail-soft:** nếu tắt, thiếu key, hoặc lời gọi lỗi, run vẫn tiếp tục với prompt
  gốc — bước planner không bao giờ làm hỏng run.
- Tắt hoàn toàn: đặt `Enabled: false`.

### Mẫu tham số (Arguments) cho provider

Chuỗi `Arguments` là một template với một trong hai placeholder:

- `{PROMPT}` — prompt được thay trực tiếp vào (đã escape). Đơn giản nhưng bị giới
  hạn bởi độ dài dòng lệnh của hệ điều hành nếu brief quá lớn.
- `{PROMPT_FILE}` — được thay bằng đường dẫn tới file tạm chứa prompt. Dùng cho
  brief lớn, ví dụ `Arguments: "exec --skip-git-repo-check - < {PROMPT_FILE}"`.

Hãy chỉnh `Command`/`Arguments` cho khớp phiên bản CLI bạn đang cài. Các cờ chạy
không tương tác khác nhau giữa các bản CLI — mặc định nhắm tới dạng phổ biến
(`codex exec ...`, `claude -p ...`). Hãy kiểm tra bằng một lần chạy thủ công.

### Cách phát hiện lỗi provider

Các CLI không cung cấp trạng thái máy-đọc-được ổn định, nên runner phân loại
stdout/stderr theo kinh nghiệm (heuristic):

- **Lỗi auth** (`unauthorized`, `not logged in`, `401`, ...) → run được đánh dấu
  `AuthError`, lưu trạng thái, gửi email.
- **Quota / rate limit** (`quota`, `rate limit`, `429`, `usage limit`, ...) → run
  được đánh dấu `QuotaLimit`, lấy gợi ý thời điểm reset nếu có, gửi email.
- **Timeout** (vượt số phút tối đa của project) → đánh dấu `Paused`.
- Các trường hợp còn lại nếu exit code khác 0 → `Failed`. Provider kế tiếp trong
  thứ tự ưu tiên sẽ được thử.

Token/cost/usage được trích xuất nếu có, nếu không thì ghi là
`Unknown / provider does not expose usage`.

---

## Resume (tiếp tục công việc dang dở)

Mỗi lần chạy lưu lại bản tóm tắt có cấu trúc của AI, task đang làm dở, nhánh hiện
tại, và session id của provider (nếu có). Lần chạy sau sẽ chèn "ngữ cảnh resume"
này vào prompt để AI tiếp tục từ chỗ dừng lại (hoặc chuyển sang task tiếp theo nếu
task cũ đã xong). Tiến độ dang dở do hết quota/timeout được giữ lại (chưa commit)
trên nhánh AI và sẽ được tiếp tục ở lần sau.

---

## Hàng rào an toàn (guardrail)

- Chạy trên một nhánh AI riêng cho mỗi lần chạy (`ai/auto/<ngày>-<runId>`) — không
  bao giờ chạy trên `main`/`master` trừ khi bật **Allow run on main** cho project.
- Sau khi AI hoàn tất, các file thay đổi được đối chiếu với các mẫu file được bảo
  vệ (`.env`, `credentials`, `*.pem`, `*.key`, private key, `.git/`). Nếu vi phạm,
  việc commit bị chặn và run bị đánh dấu thất bại.
- Runner không bao giờ force-push hay chạy lệnh git phá hủy. Auto-commit và
  auto-push là **tùy chọn bật** theo từng project (mặc định push tắt).
- Các guardrail này cũng được nêu rõ trong prompt gửi cho AI.

---

## Cài đặt (Windows Task Scheduler)

Mở PowerShell với quyền **Administrator**:

```powershell
# build/publish + đăng ký scheduled task
.\scripts\installer.ps1                  # mặc định: mỗi 5 giờ + dashboard khi đăng nhập
.\scripts\installer.ps1 -IntervalHours 8 # tùy chỉnh chu kỳ
.\scripts\installer.ps1 -NoDashboard     # chỉ tạo task chạy định kỳ
.\scripts\installer.ps1 -SkipBuild       # dùng lại thư mục .\publish có sẵn

# gỡ bỏ các scheduled task
.\scripts\uninstaller.ps1
```

`installer.ps1` publish ra `./publish` và đăng ký hai scheduled task, cả hai đều
chạy dưới danh nghĩa **bạn**, chỉ khi bạn đang đăng nhập (để Codex/Claude CLI dùng
được phiên đăng nhập sẵn có của bạn):

| Task | Kích hoạt | Hành động |
| ---- | --------- | --------- |
| `AutoDevRunner-Run` | mỗi *IntervalHours* | `AutoDevRunner.exe --run-due` |
| `AutoDevRunner-Dashboard` | khi đăng nhập | `AutoDevRunner.exe` (web UI tại http://localhost:5099) |

Kích hoạt một lần chạy bất kỳ lúc nào:

```powershell
Start-ScheduledTask -TaskName AutoDevRunner-Run
```

`uninstaller.ps1` dừng và xóa cả hai task. Thư mục `./publish` và database
PostgreSQL không bị đụng tới.

> App vẫn có thể chạy như một Windows Service (`builder.Host.UseWindowsService`)
> hoặc dùng scheduler nội bộ (`AutoDev:Scheduler:Enabled = true`) nếu bạn không
> muốn dùng Task Scheduler.

---

## Web API

Gốc: `http://localhost:5099/api` (chỉ bind vào localhost; không auth trong MVP).

| Method | Route | Mục đích |
| ------ | ----- | -------- |
| GET  | `/overview` | số liệu tổng quan, lần chạy gần nhất, trạng thái provider |
| GET  | `/projects` | danh sách project |
| POST | `/projects` | tạo project |
| GET  | `/projects/{id}` | chi tiết project + các lần chạy gần đây |
| PUT  | `/projects/{id}` | cập nhật project / ghi chú / cài đặt |
| DELETE | `/projects/{id}` | xóa project |
| POST | `/projects/{id}/run` | kích hoạt một lần chạy ngay |
| POST | `/projects/{id}/{pause\|resume\|enable\|disable}` | đổi trạng thái |
| GET  | `/runs?projectId=&take=` | lịch sử chạy |
| GET  | `/runs/{id}` | chi tiết lần chạy (tóm tắt, file thay đổi, validation) |
| GET  | `/runs/{id}/log` | log đầy đủ của lần chạy |
| GET  | `/providers` | trạng thái provider |
| GET  | `/settings` | cấu hình scheduler/email/provider đang áp dụng |
| GET  | `/health` | kiểm tra tình trạng hoạt động |

---

## Cấu trúc dự án

```
src/AutoDevRunner/
  Program.cs              # host + DI + chế độ một lần --run-due + routing
  appsettings.json        # connection string / providers / cấu hình email
  Config/                 # các lớp option có kiểu rõ ràng
  Data/AppDbContext.cs    # EF Core (PostgreSQL / Npgsql)
  Models/                 # Project, RunRecord, ProviderState, enums
  Providers/              # ProcessRunner, adapter CLI, bộ phân tích output, registry
  Services/               # orchestrator, git, guardrail, prompt, summary, email,
                          #   DueProjectsRunner, scheduler, run lock/launcher
  Api/                    # endpoint minimal-API + DTO
  wwwroot/                # dashboard (index.html, app.js, styles.css)
scripts/                  # installer.ps1 / uninstaller.ps1 (Windows Task Scheduler)
docs/                     # tài liệu kế hoạch + mẫu brief
```

---

## Ghi chú & giới hạn (MVP)

- Không có xác thực — chỉ bind vào `localhost`. Hãy thêm auth trước khi mở ra LAN.
- Cờ của provider khác nhau theo phiên bản CLI; hãy xác nhận template `Arguments`
  hoạt động bằng một lần chạy thủ công trước khi tin tưởng vào scheduled task.
- Validation chỉ chạy một lệnh shell; nếu cần nhiều bước thì gộp vào một script.
- Scheduled task chỉ chạy khi bạn đang đăng nhập (để CLI có phiên của bạn). Để
  chạy 24/7 không cần đăng nhập, hãy chạy host như một Windows Service và bật
  scheduler nội bộ thay thế.
