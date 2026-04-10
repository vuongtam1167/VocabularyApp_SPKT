# Setup SQL Server cho Vocab_LearningApp

## 1. Chuẩn bị SQL Server

1. Mở SQL Server / SSMS.
2. Kết nối tới server:
   - `Server Name`: `.\SQLEXPRESS`
   - `Authentication`: `SQL Server Authentication`
   - `User Name`: `sa`
   - `Password`: `123456`
   - `Trust Server Certificate`: bật

## 2. Import database mẫu

1. Mở file `C:\Users\Admin\Desktop\Vocabulary_database_beta.sql`.
2. Chạy toàn bộ script.
3. Script sẽ tự:
   - tạo database `VocabularyLearningApp`
   - tạo các bảng `Users`, `Decks`, `Vocabularies`, `Learning_Progress`, `Study_Logs`, `User_Streaks`, `Notifications`
   - thêm sample data

## 3. Kiểm tra connection string trong project

File: `appsettings.json`

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=.\\SQLEXPRESS;Database=VocabularyLearningApp;User Id=sa;Password=123456;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True"
}
```

## 4. Chạy ứng dụng

```powershell
dotnet restore .\Vocab_LearningApp.csproj /p:NuGetAudit=false --ignore-failed-sources
dotnet build .\Vocab_LearningApp.csproj --no-restore /nr:false /m:1 /p:UseSharedCompilation=false
dotnet run
```

## 5. Chức năng đã nối vào database

- MVC pages:
  - `/Home/Login`
  - `/Home/Dashboard`
  - `/Home/Vocabulary`
  - `/Home/Vocab_detail?deckId={id}`
  - `/Home/Learning?deckId={id}`
  - `/Home/Progress`

- API:
  - `POST /api/auth/login`
  - `POST /api/auth/register`
  - `POST /api/auth/google`
  - `GET /api/auth/me`
  - `GET /api/dashboard`
  - `GET /api/decks`
  - `GET /api/decks/{deckId}`
  - `POST /api/decks`
  - `PUT /api/decks/{deckId}`
  - `DELETE /api/decks/{deckId}`
  - `POST /api/decks/{deckId}/vocabularies`
  - `PUT /api/decks/vocabularies/{vocabularyId}`
  - `DELETE /api/decks/vocabularies/{vocabularyId}`
  - `POST /api/decks/import`
  - `GET /api/decks/{deckId}/export?format=csv`
  - `GET /api/decks/{deckId}/export?format=xlsx`
  - `GET /api/learning/session?deckId={id}`
  - `POST /api/learning/review`
  - `GET /api/progress`
  - `GET /api/notifications`
  - `POST /api/notifications/{id}/read`

## 6. Ghi chú

- Mật khẩu local account được hash bằng `bcrypt`.
- Xác thực dùng `JWT`, token được lưu trong cookie `access_token`.
- Google login đã có API + schema database, nhưng để chạy OAuth thật cần bổ sung Google Client ID/Secret.
- Thuật toán SRS đang dùng SM-2 và lưu kết quả vào `Learning_Progress`.
