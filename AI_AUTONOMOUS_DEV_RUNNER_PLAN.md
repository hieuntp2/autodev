# Build Project: AI Autonomous Dev Runner

Xây dựng một ứng dụng chạy trên Windows gồm:

- Windows Service / Worker Service
- Local Web API
- Local Web Dashboard
- Adapter cho Codex CLI
- Adapter cho Claude CLI
- Cơ chế schedule, resume, logging, email report

## Mục tiêu

Ứng dụng sẽ chạy định kỳ mỗi 5 giờ hoặc mỗi ngày để tự động sử dụng Codex CLI và Claude CLI đã được đăng nhập sẵn trên máy.

Service sẽ đọc một file mô tả mục tiêu ban đầu của từng project, ví dụ `ai-autonomous.md`. Dựa vào file này, AI được phép tự đọc codebase, tự lập plan, tự chọn task, tự code, tự test, tự refactor, tự viết tài liệu, tự nghĩ thêm tính năng mới phù hợp với mục tiêu project.

AI không cần hỏi approval trước khi thực hiện task.

Mục tiêu là tận dụng quota/token AI còn lại để tự động phát triển project hoặc công cụ cá nhân.

## Provider Requirement

Ứng dụng phải hỗ trợ cả:

- Codex CLI
- Claude CLI

Codex CLI là provider chính, không được bỏ qua.

Nếu Codex không có cơ chế check remaining quota thì không cần check trước. Service cứ gọi Codex chạy thử. Nếu Codex báo hết quota, bị rate limit, lỗi auth hoặc lỗi provider thì service dừng run hiện tại, lưu state, gửi email và chờ lần chạy sau.

Claude CLI cũng hoạt động tương tự. Nếu có thể lấy token/cost/usage thì lưu lại. Nếu không lấy được thì ghi là unknown.

## Autonomous Behavior

AI được phép:

- Tự đọc project.
- Tự hiểu kiến trúc.
- Tự tạo backlog.
- Tự chọn task có giá trị nhất.
- Tự implement.
- Tự viết test.
- Tự fix lỗi build/test.
- Tự refactor.
- Tự tạo tài liệu.
- Tự nghĩ thêm feature mới.
- Tự chia nhỏ task lớn.
- Tự resume task đang dang dở.

Không cần user approve từng task.

Tuy nhiên service vẫn phải có guardrail kỹ thuật:

- Không chạy trực tiếp trên branch chính nếu cấu hình cấm.
- Tạo branch riêng cho AI run.
- Không sửa file secret như `.env`, credentials, private key.
- Không chạy command nguy hiểm như xóa toàn bộ project, format disk, `git reset --hard`.
- Không auto-push trừ khi được bật trong config.
- Sau mỗi run phải có summary rõ ràng.

## Project Input

Mỗi project có một file mô tả, ví dụ:

`ai-autonomous.md`

File này mô tả:

- Mục tiêu sản phẩm.
- Tech stack.
- Tình trạng hiện tại.
- Hướng phát triển mong muốn.
- Những việc AI được phép làm.
- Những việc AI không được làm.
- Lệnh build/test nếu có.
- Ghi chú business/product nếu có.

Nếu không có backlog rõ ràng, AI tự tạo backlog và chọn task đầu tiên để làm.

## Resume Requirement

Service phải lưu được trạng thái mỗi lần chạy.

Nếu task đang làm mà bị hết quota, timeout, crash, lỗi mạng hoặc provider limit thì lần chạy sau có thể tiếp tục.

Resume có thể dựa trên:

- Provider session id nếu CLI có hỗ trợ.
- State file nội bộ.
- Summary lần chạy trước.
- Git branch hiện tại.
- Log stdout/stderr.
- File `.ai-runner/runs/...md`.

Nếu provider không hỗ trợ resume session trực tiếp thì tạo prompt resume mới dựa trên state và summary.

## Email Report

Sau mỗi lần chạy, service phải gửi email tóm tắt.

Email gồm:

- Project name.
- Provider đã dùng: Codex hoặc Claude.
- Branch hiện tại.
- Run status: success, paused, failed, quota limit, auth error.
- Tính năng/task đã làm.
- Tính năng chưa làm.
- Ý tưởng mới AI tự nghĩ ra.
- File chính đã thay đổi.
- Test/build đã chạy và kết quả.
- Token/cost/usage đã dùng nếu lấy được.
- Nếu không lấy được token thì ghi `Unknown / provider does not expose usage`.
- Nếu bị limit thì ghi reset time nếu parse được.

## Local Web API

Tạo một Web API local chạy cùng ứng dụng hoặc chạy riêng.

Mục tiêu Web API là để dashboard local có thể đọc/cập nhật thông tin đơn giản.

Không cần login/authentication trong MVP.

API chỉ cần bind mặc định vào `localhost`, không public ra internet.

Web API cần hỗ trợ:

- Xem danh sách project.
- Xem trạng thái project.
- Xem lịch sử các run.
- Xem run summary.
- Xem provider status.
- Xem task đang chạy/dang dở.
- Pause/resume project.
- Enable/disable project.
- Trigger run thủ công.
- Update ghi chú project.
- Update priority đơn giản.
- Xem lỗi gần nhất.
- Xem email report gần nhất.
- Xem token/cost/usage nếu có.
- Xem log gần nhất.

Nếu sau này muốn expose qua LAN hoặc internet thì phải thêm authentication, nhưng MVP không cần login.

## Local Web Dashboard

Tạo một web local đơn giản để truy cập Web API.

Dashboard không cần login.

Dashboard nên có các màn hình:

### 1. Overview

Hiển thị:

- Tổng số project.
- Project đang enabled.
- Project đang paused.
- Run gần nhất.
- Provider gần nhất đã dùng.
- Lỗi gần nhất.
- Quota/usage gần nhất nếu có.

### 2. Projects

Hiển thị danh sách project:

- Project name.
- Path.
- Enabled/disabled.
- Priority.
- Last run status.
- Last provider.
- Last run time.
- Current branch.
- Current task.
- Nút trigger run.
- Nút pause/resume.
- Nút enable/disable.

### 3. Project Detail

Hiển thị chi tiết một project:

- Project brief path.
- Current state.
- Current task.
- Latest summary.
- Pending ideas.
- Completed tasks.
- Failed tasks.
- Recent changed files.
- Validation result.
- Notes field để user cập nhật ghi chú đơn giản.

### 4. Runs

Hiển thị lịch sử run:

- Run id.
- Project.
- Provider.
- Start time.
- End time.
- Status.
- Reason nếu fail/pause.
- Usage/token/cost nếu có.
- Link xem summary/log.

### 5. Providers

Hiển thị:

- Codex enabled/disabled.
- Claude enabled/disabled.
- Last successful run.
- Last auth error.
- Last quota limit.
- Last known usage/cost nếu có.

### 6. Settings

Cho phép chỉnh một số thông tin đơn giản:

- Enable/disable project.
- Priority project.
- Schedule interval.
- Max run minutes.
- Email recipient.
- Provider priority.
- Auto commit on/off.
- Auto push on/off.

MVP chỉ cần settings đơn giản, có thể lưu vào file hoặc SQLite.

## Storage

Dùng SQLite hoặc file-based storage đều được, nhưng nên ưu tiên SQLite nếu không làm phức tạp.

Cần lưu:

- Project config.
- Provider config.
- Run history.
- Current state.
- Summary path.
- Log path.
- Usage/cost nếu có.
- User notes.
- Email report status.

## Git Behavior

Service cần làm việc an toàn với Git:

- Kiểm tra repo hợp lệ.
- Không chạy trên main/master nếu bị cấm.
- Tạo branch AI riêng.
- Ghi nhận changed files.
- Auto commit nếu cấu hình bật.
- Commit message rõ ràng.
- Không auto push trong MVP, trừ khi config bật.

## Validation

Sau khi AI chạy xong, service nên chạy validation command nếu có.

Ví dụ:

- Backend test.
- Frontend build.
- Unit test.
- Lint nếu có.

Nếu validation fail:

- Không coi là task hoàn tất hoàn toàn.
- Lưu lỗi.
- Gửi email.
- Lần sau AI có thể ưu tiên fix lỗi đó.

## MVP Acceptance Criteria

MVP hoàn thành khi có:

- Windows Worker Service chạy được.
- Local Web API chạy được.
- Local Web Dashboard truy cập được qua browser.
- Không cần login.
- Thêm/sửa project đơn giản.
- Trigger run thủ công từ dashboard.
- Schedule tự chạy.
- Codex CLI adapter hoạt động.
- Claude CLI adapter hoạt động.
- Nếu provider hết quota thì pause và lưu state.
- Nếu provider auth lỗi thì gửi email.
- Có run summary sau mỗi lần chạy.
- Có email report sau mỗi lần chạy.
- Có resume task dang dở.
- Có lịch sử run trên dashboard.
- Có trạng thái provider trên dashboard.
- Có guardrail tránh phá project.