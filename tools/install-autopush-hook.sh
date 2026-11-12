#!/bin/sh
# =============================================================================
#  安装 CDiskWatch 仓库级 post-commit 自动推送钩子（走 SSH）
#
#  用法：
#      bash tools/install-autopush-hook.sh          # 安装（幂等，可重复执行）
#      bash tools/install-autopush-hook.sh --check  # 只检查状态，不改动
#      bash tools/install-autopush-hook.sh --uninstall
#
#  作用：
#    1. 把 tools/git-hooks/post-commit 复制到 .git/hooks/post-commit
#    2. 确保 github / gitee 远端使用 SSH 地址（git@host:owner/repo.git）
#
#  为什么需要它：
#    全局钩子（~/.githooks/post-commit，靠 core.hooksPath 挂载）在远端是 HTTPS 时
#    会弹用户名/密码框，无 TTY 环境（CI、自动化）下必然失败。
#    仓库级钩子在推送前把远端 URL 强制规范成 SSH，因此没有交互式认证失败的路径。
#
#  注意：全局 core.hooksPath 指向 ~/.githooks 时，仓库 .git/hooks/ 下的钩子不会被
#  自动触发——但全局 post-commit 会主动转交执行仓库里的 .git/hooks/post-commit，
#  所以本仓库的钩子依然会生效。
# =============================================================================

set -u
say() { printf '[install-autopush] %s\n' "$*"; }

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || {
  say "错误：不在 git 仓库里"; exit 1; }
gitdir=$(git rev-parse --git-dir 2>/dev/null) || exit 1
case "$gitdir" in /*) ;; *) gitdir="$repo_root/$gitdir" ;; esac

src="$repo_root/tools/git-hooks/post-commit"
dst="$gitdir/hooks/post-commit"
mode="${1:-install}"

# ------------------------------------------------ SSH 规范化远端 URL
use_ssh_remotes() {
  changed=0
  for r in $(git remote 2>/dev/null); do
    url=$(git remote get-url "$r" 2>/dev/null) || continue
    case "$url" in
      git@*|ssh://*) continue ;;
    esac
    slug=""
    case "$url" in
      *github.com*) slug=$(printf '%s' "$url" | sed -e 's#^.*github.com[:/]##' -e 's#\.git$##') ;;
      *gitee.com*)  slug=$(printf '%s' "$url" | sed -e 's#^.*gitee.com[:/]##'  -e 's#\.git$##') ;;
    esac
    [ -n "$slug" ] || continue
    host=$(printf '%s' "$url" | sed -e 's#^.*\.\(github\.com\|gitee\.com\).*$#\1#')
    [ -n "$host" ] || continue
    say "远端 $r: $url → git@$host:$slug.git"
    git remote set-url "$r" "git@$host:$slug.git" && changed=1
  done
  [ "$changed" = "1" ] || say "所有远端已是 SSH 地址"
}

status() {
  say "仓库      : $repo_root"
  say "钩子源    : $src"
  say "钩子目标  : $dst"
  if [ -x "$dst" ]; then
    say "状态      : 已安装（可执行）"
  elif [ -f "$dst" ]; then
    say "状态      : 已存在但没有可执行权限 → chmod +x"
  else
    say "状态      : 未安装"
  fi
  say "全局 hooksPath : $(git config --get core.hooksPath 2>/dev/null || echo '(未设置)')"
  say "远端："
  for r in $(git remote 2>/dev/null); do
    say "  $r -> $(git remote get-url "$r" 2>/dev/null)"
  done
}

case "$mode" in
  --check)
    status
    ;;
  --uninstall)
    if [ -f "$dst" ]; then
      rm -f "$dst" && say "已删除 $dst"
    else
      say "未安装，无需卸载"
    fi
    ;;
  install|"")
    if [ ! -f "$src" ]; then say "错误：找不到钩子源 $src"; exit 1; fi
    mkdir -p "$(dirname "$dst")"
    cp "$src" "$dst"
    chmod +x "$dst"
    say "已安装：$dst"
    use_ssh_remotes
    status
    ;;
  *)
    say "未知参数：$mode（可用：install / --check / --uninstall）"; exit 1 ;;
esac
exit 0
