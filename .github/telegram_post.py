# Тихий ПК — складає допис про реліз і надсилає його в Telegram-канал (викликається з telegram.yml).
import html, json, os, subprocess, sys, urllib.request

token = os.environ.get("TELEGRAM_BOT_TOKEN", "")
chat = os.environ.get("TELEGRAM_CHAT_ID", "")
tag = os.environ.get("TAG", "")
repo = os.environ.get("GITHUB_REPOSITORY", "Dimonchikk/TyhyiPK")
if not token or not chat:
    print("::warning::Немає секретів TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID — допис не надіслано.")
    sys.exit(0)

rel = json.loads(subprocess.check_output(["gh", "api", f"repos/{repo}/releases/tags/{tag}"]))
version = tag.lstrip("vV")
body = (rel.get("body") or "").replace("\r", "")
# Беремо лише список змін (без рядка «Завантаж … нижче», бо в Telegram є кнопка).
lines = [l for l in body.split("\n") if l.strip() and not l.startswith("Завантаж ")]

def fmt(line):
    t = html.escape(line.strip())
    if t.startswith("- ") or t.startswith("* "):
        t = "• " + t[2:]
    return t

user = repo.split("/")[0].lower()
name = repo.split("/")[1]
site = f"https://{user}.github.io/{name}/"
download = f"https://github.com/{repo}/releases/download/{tag}/Tykhyi-PK-setup.exe"

text = f"🌀 <b>Тихий ПК {html.escape(version)}</b>\n\n<b>Що нового:</b>\n" + "\n".join(fmt(l) for l in lines)
text += "\n\nУ програмі натисни «Шукати оновлення» внизу меню — або завантаж інсталятор кнопкою нижче."

payload = {
    "chat_id": chat,
    "text": text,
    "parse_mode": "HTML",
    "disable_web_page_preview": True,
    "reply_markup": {"inline_keyboard": [[
        {"text": f"⬇️ Завантажити {version}", "url": download},
        {"text": "🌐 Сайт", "url": site},
    ]]},
}
req = urllib.request.Request(f"https://api.telegram.org/bot{token}/sendMessage",
                             data=json.dumps(payload).encode("utf-8"),
                             headers={"Content-Type": "application/json"})
try:
    with urllib.request.urlopen(req, timeout=30) as r:
        print("Надіслано:", json.loads(r.read()).get("ok"))
except urllib.error.HTTPError as e:
    print("::error::Telegram відповів:", e.code, e.read().decode("utf-8", "replace"))
    sys.exit(1)
