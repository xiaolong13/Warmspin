# 下载站部署说明

这个文件夹就是一个纯静态网站，`index.html` 打开即用，不依赖任何后端。

```
website/
  index.html          首页（内联 CSS，无外部依赖）
  assets/             截图
  download/Warmspin.exe   供下载的程序
  update.json         自动更新清单（需放到能公开访问的地址）
```

## 三种上线方式

### 1. GitHub Pages（推荐，免费且和你开源的需求一致）

1. 把整个仓库推到 GitHub（`website/` 一起推）
2. 仓库 Settings → Pages → Source 选 `main` 分支、目录选 `/website`
3. 等一两分钟，访问 `https://<用户名>.github.io/Warmspin/`

### 2. 任意静态托管

把 `website/` 目录上传到 Vercel / Netlify / Cloudflare Pages / 自己的 Nginx，
把 `index.html` 设成入口即可。

### 3. 本地先看效果

直接双击 `website/index.html`，或者：

```powershell
cd website
python -m http.server 8080     # 有 Python 的话
# 浏览器打开 http://127.0.0.1:8080
```

## 让自动更新真正生效

1. 把 `update.json` 放到一个能公开访问的地址（比如 Pages 根目录）
2. 改 `src/Updater.cs` 里的 `ManifestUrl` 指向它
3. 重新编译

清单格式：

```json
{
  "version": "2.1.0",
  "url": "https://你的域名/download/Warmspin.exe",
  "sha256": "下载文件的 sha256",
  "notes": "这次改了什么"
}
```

程序会比较版本号，发现新版就下载、校验 SHA-256、改名替换并重启。
清单不存在（404）时会显示「已是最新版本」，不会报错。

## 关于"未知发布者"提示

`Warmspin.exe` 没有代码签名证书，Windows 会对任何未签名的新程序弹"无法验证发布者"。
这不代表文件有问题。想彻底消除提示，需要买一张代码签名证书（约 ¥1000+/年），
用 `signtool` 签名后再发布：

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a dist\Warmspin.exe
```

在没有证书之前，网页上的 SHA-256 就是给用户自查用的。
