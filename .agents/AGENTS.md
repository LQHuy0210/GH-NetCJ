# GHNet India — hướng dẫn dự án

Cập nhật: 12/08/2026.

## Nguồn sự thật

Đọc `README.md` và `docs/README.md` trước khi sửa. Mục tiêu là tùy biến WPF `GH-Net` thành flow Camera → Printer trực tiếp, không xây lại UI hoặc licensing. Markdown ở root chỉ là compatibility pointer.

## Kiến trúc

```text
GH-Net (WPF/MVVM/DI) -> GH-Net.Service -> GH-Net.Repository -> GH-Net.Core
                    \-> GH-Net.Licensing
```

- Không tạo dependency ngược hoặc service locator.
- Mapping thuần ở Core; transaction/FIFO/lifecycle ở Service; TCP ở Repository; UI chỉ điều khiển/hiển thị.
- `GH-Net` là executable duy nhất; prototype `GH-Net.CameraPrinter` đã bị xóa.

## Flow đích

```text
camera frame -> bounded FIFO -> cắt range -> persist JobData
             -> DATA -> DATA;RYES -> RSFP -> Printed
```

- Job trực tiếp không cần CSV/XLSX; mỗi frame hợp lệ tạo một dòng.
- Start/End 1-based, inclusive; mọi ký tự gồm `;` đều được tính.
- Persist trước send; một consumer giữ FIFO.
- `DATA;RYES` chỉ là Received; `RSFP` mới là Printed.
- Frame ngắn/mapping lỗi không được gửi máy in.

## Bắt buộc giữ

- WPF shell, LightTheme, localization vi/en và assets còn reference.
- `GH-Net.Licensing`, DPAPI, hardware ID và activation flow.
- Camera/printer singleton configuration và Apply semantics.
- EF migration history và dữ liệu hiện hữu.

## Cleanup

Đọc `docs/CHANGE_MATRIX.md` và cleanup-safety trước khi xóa. Không xóa mục Unknown. Với mỗi target phải search C#, XAML binding, DI, csproj/slnx, migration, test, docs và packaging; phải có requirement loại bỏ, replacement và verification. Không xóa migration lịch sử. WPF có reflection/binding/resource nên thiếu direct reference chưa chứng minh dead code.

## Database và Service

- Write qua UnitOfWork; singleton runtime dùng `IUnitOfWorkFactory`, không giữ DbContext dài hạn.
- Entity mới cần DbSet, Fluent config, migration, snapshot và fresh-database test; dự án India không hỗ trợ upgrade database GH-Net cũ.
- Queue bounded/single consumer; shutdown drain theo `docs/FLOW.md`.
- `ICameraToPrinterRuntimeService` là runtime trực tiếp: persist frame trước send, xử lý FIFO/ACK/FULL và dùng RSFP đánh dấu Printed.
- Runtime trực tiếp dùng `IUnitOfWorkFactory`; mỗi transition database sở hữu UnitOfWork/transaction riêng.
- `IJobRuntimeCoordinator` route `CameraToPrinter`: validate cả hai endpoint trước protocol, gửi STOP/STAR, mở runtime, sở hữu Job status và relay background failure.
- Event observer không được phá transition. UI event batching qua Dispatcher; không reload toàn bộ dữ liệu mỗi frame.
- Async API nhận và forward `CancellationToken`; timeout/retry tập trung trong policy.

## WPF và license

- MVVM và constructor injection; code-behind chỉ cho interaction UI.
- Text mới có cả vi-VN/en-US; style/brush lấy resource.
- UI realtime dùng sliding window; SQLite giữ lịch sử.
- Mode trực tiếp cần đồng thời feature Camera và Printer, phải disable sớm khi license thiếu.

## Verification

Theo `docs/TEST_PLAN.md`. Mỗi slice cần focused smoke, Release build và stale-reference search. Không xóa test trước feature. Không báo hoàn tất khi build/migration/test/publish liên quan thất bại.

## Đồng bộ tài liệu

Behavior thay đổi phải cập nhật file tương ứng trong `docs/`: requirements, flow, architecture, database, protocols, UI, change matrix, implementation, tests hoặc release.
