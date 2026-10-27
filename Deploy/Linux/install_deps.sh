#!/usr/bin/env bash
# ============================================================================
# 车载监控程序 Linux 依赖检查 / 安装脚本
#
# 程序里的车载视频由 libVLC 播放，打包进程序的 VLC 插件需要系统里装有对应的
# ffmpeg / libva 等库。目标机缺少这些依赖时，视频会在开始解码时直接崩溃
# (Caught fatal signal 11)。部署到新机器时先运行本脚本。
#
# 用法（把本脚本拷到程序可执行文件同级目录，或用 -d 指定程序目录）：
#   chmod +x install_deps.sh
#   ./install_deps.sh                检查；发现缺少软件包时询问是否现在安装
#   ./install_deps.sh check          只检查，不修改系统，也不询问
#   ./install_deps.sh install        检查并直接安装缺少的软件包（需要 root 或 sudo）
#   ./install_deps.sh install -y     安装时不再询问
#
# 也可以在文件管理器里双击运行（选"在终端中运行"）：结束后会停在
# "按回车键关闭窗口"，不会一闪而过。
#
# 选项：
#   -d, --dir DIR    程序所在目录（含 xxx_Data 的目录），默认是本脚本所在目录
#   -y, --yes        安装时自动确认
#   --update         安装前先执行 apt-get update（离线机器不要加）
#   --pause          结束时等待按回车（双击运行时会自动等待）
#   --no-pause       结束时不等待
#   -h, --help       显示帮助
#
# 退出码：0 = 全部正常；1 = 有缺失或问题；2 = 参数错误
# ============================================================================

set -u

# 经验证可用的依赖包（Ubuntu 20.04 / 麒麟 V10 SP1，对应项目里自带的 VLC 3.0.9.2）
PKGS=(vlc libvlc5 vlc-plugin-base vlc-plugin-access-extra)

MODE="check"
MODE_EXPLICIT=0      # 命令行是否明确指定了 check / install
ASSUME_YES=0
DO_UPDATE=0
PAUSE_MODE="auto"    # auto = 双击运行时等待；yes / no 由 --pause / --no-pause 指定
GAME_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ---------- 输出样式 ----------
if [ -t 1 ]; then
    C_OK=$'\033[32m'; C_WARN=$'\033[33m'; C_ERR=$'\033[31m'; C_END=$'\033[0m'
else
    C_OK=""; C_WARN=""; C_ERR=""; C_END=""
fi
PROBLEMS=0
say_ok()   { echo "  ${C_OK}[ OK ]${C_END} $*"; }
say_warn() { echo "  ${C_WARN}[警告]${C_END} $*"; }
say_err()  { echo "  ${C_ERR}[缺失]${C_END} $*"; PROBLEMS=$((PROBLEMS + 1)); }
say_info() { echo "         $*"; }
title()    { echo; echo "== $* =="; }

usage() {
    sed -n '2,/^# =====.*$/p' "${BASH_SOURCE[0]}" | sed '1d;$d' | sed 's/^# \{0,1\}//'
}

# ---------- 参数 ----------
while [ $# -gt 0 ]; do
    case "$1" in
        check|install) MODE="$1"; MODE_EXPLICIT=1 ;;
        -d|--dir)      shift; GAME_DIR="${1:-}" ;;
        -y|--yes)      ASSUME_YES=1 ;;
        --update)      DO_UPDATE=1 ;;
        --pause)       PAUSE_MODE="yes" ;;
        --no-pause)    PAUSE_MODE="no" ;;
        -h|--help)     usage; exit 0 ;;
        *)             echo "未知参数：$1"; echo; usage; exit 2 ;;
    esac
    shift
done

if [ -z "$GAME_DIR" ] || [ ! -d "$GAME_DIR" ]; then
    echo "程序目录不存在：${GAME_DIR:-<空>}"
    exit 2
fi

MISSING=()

# ---------- 结束时暂停（双击运行时窗口会在脚本结束后立刻关闭，看不到结果）----------
# auto：只有在"有终端，且父进程不是常见 shell"（即不是从命令行里敲出来的）时才暂停
should_pause() {
    case "$PAUSE_MODE" in
        yes) return 0 ;;
        no)  return 1 ;;
    esac
    [ -t 0 ] && [ -t 1 ] || return 1
    local parent
    parent="$(ps -o comm= -p "$PPID" 2>/dev/null | tr -d ' ')"
    case "$parent" in
        bash|sh|dash|zsh|fish|ksh|sudo|su|ssh|sshd|tmux|screen) return 1 ;;
    esac
    return 0
}

pause_at_exit() {
    if should_pause; then
        echo
        read -r -p "按回车键关闭窗口..." _ || true
    fi
}
trap pause_at_exit EXIT

# ---------- 1. 系统信息 ----------
check_system() {
    title "系统信息"
    local arch
    arch="$(uname -m)"
    if [ "$arch" = "x86_64" ]; then
        say_ok "CPU 架构：$arch"
    else
        say_err "CPU 架构是 $arch，程序里的 VLC 库只支持 x86_64"
    fi

    if [ -r /etc/os-release ]; then
        # shellcheck disable=SC1091
        . /etc/os-release
        say_info "系统：${PRETTY_NAME:-未知}"
    fi
    if command -v ldd >/dev/null 2>&1; then
        say_info "glibc：$(ldd --version 2>&1 | head -n 1)"
    fi
}

# ---------- 2. 系统软件包 ----------
check_packages() {
    title "系统软件包"
    MISSING=()

    if ! command -v dpkg-query >/dev/null 2>&1; then
        say_warn "没有 dpkg，跳过软件包检查（本脚本只支持 Debian/Ubuntu/麒麟 这类 apt 系统）"
        return
    fi

    local p status ver
    for p in "${PKGS[@]}"; do
        status="$(dpkg-query -W -f='${Status}' "$p" 2>/dev/null || true)"
        if [ "$status" = "install ok installed" ]; then
            ver="$(dpkg-query -W -f='${Version}' "$p" 2>/dev/null)"
            say_ok "$p  ($ver)"
        else
            say_err "$p  未安装"
            MISSING+=("$p")
        fi
    done

    # 供对照的依赖库版本（不参与判断）
    local libs
    libs="$(dpkg-query -W -f='${db:Status-Abbrev} ${Package} ${Version}\n' 'libavcodec5*' 'libavutil5*' 'libswscale*' 'libva2' 2>/dev/null | awk '$1=="ii"{print "         " $2 "  " $3}')"
    if [ -n "$libs" ]; then
        say_info "相关库版本（排查问题时对照用）："
        echo "$libs"
    fi
}

# ---------- 3. 程序自带的库 ----------
check_bundled() {
    title "程序自带的 VLC 库"

    local data_dir="" d
    for d in "$GAME_DIR"/*_Data; do
        if [ -d "$d/Plugins/x86_64" ]; then
            data_dir="$d"
            break
        fi
    done

    if [ -z "$data_dir" ]; then
        say_warn "在 $GAME_DIR 下没找到 *_Data/Plugins/x86_64，跳过本项（用 -d 指定程序目录）"
        return
    fi

    local plug_dir="$data_dir/Plugins/x86_64"
    say_info "程序数据目录：$data_dir"

    local f missing_libs
    for f in libvlc.so libvlccore.so UniversalMediaPlayer.so; do
        if [ ! -f "$plug_dir/$f" ]; then
            say_err "缺少文件 Plugins/x86_64/$f"
            continue
        fi
        if ! command -v ldd >/dev/null 2>&1; then
            say_ok "$f"
            continue
        fi
        missing_libs="$(LD_LIBRARY_PATH="$plug_dir:${LD_LIBRARY_PATH:-}" ldd "$plug_dir/$f" 2>&1 | grep -E "not found|没有那个文件" || true)"
        if [ -n "$missing_libs" ]; then
            say_err "$f 依赖的库找不到："
            echo "$missing_libs" | sed 's/^/           /'
        else
            say_ok "$f"
        fi
    done

    local vlc_plugins="$plug_dir/vlc/plugins"
    if [ ! -d "$vlc_plugins" ]; then
        say_err "缺少 VLC 插件目录 Plugins/x86_64/vlc/plugins"
    else
        say_ok "VLC 插件目录（$(find "$vlc_plugins" -name '*.so' 2>/dev/null | wc -l) 个插件）"

        # 解码插件依赖系统 ffmpeg，缺库是最常见的崩溃原因
        local avc
        avc="$(find "$vlc_plugins" -name 'libavcodec_plugin.so' 2>/dev/null | head -n 1)"
        if [ -n "$avc" ] && command -v ldd >/dev/null 2>&1; then
            missing_libs="$(LD_LIBRARY_PATH="$plug_dir:${LD_LIBRARY_PATH:-}" ldd "$avc" 2>&1 | grep -E "not found|没有那个文件" || true)"
            if [ -n "$missing_libs" ]; then
                say_err "解码插件 libavcodec_plugin.so 依赖的库找不到："
                echo "$missing_libs" | sed 's/^/           /'
            else
                say_ok "解码插件 libavcodec_plugin.so 的依赖完整"
            fi
        fi
    fi

    title "程序配置文件"
    local sa="$data_dir/StreamingAssets"
    for f in CarSetting.txt AppSetting.txt; do
        if [ -f "$sa/$f" ]; then
            say_ok "StreamingAssets/$f"
        else
            say_warn "StreamingAssets/$f 不存在（缺失时使用默认值，或功能不可用）"
        fi
    done
}

# ---------- 4. 安装 ----------
do_install() {
    title "安装缺少的软件包"

    if [ "${#MISSING[@]}" -eq 0 ]; then
        say_ok "没有需要安装的软件包"
        return 0
    fi
    if ! command -v apt-get >/dev/null 2>&1; then
        say_err "没有 apt-get，无法自动安装，请手动安装：${MISSING[*]}"
        return 1
    fi

    local sudo_cmd=""
    if [ "$(id -u)" -ne 0 ]; then
        if command -v sudo >/dev/null 2>&1; then
            sudo_cmd="sudo"
        else
            say_err "不是 root 用户，且系统没有 sudo，请用 root 重新运行"
            return 1
        fi
    fi

    echo "  将安装：${MISSING[*]}"
    if [ "$DO_UPDATE" -eq 1 ]; then
        $sudo_cmd apt-get update || say_warn "apt-get update 失败，继续尝试安装"
    fi

    local yes_opt=""
    [ "$ASSUME_YES" -eq 1 ] && yes_opt="-y"

    # shellcheck disable=SC2086
    if $sudo_cmd apt-get install $yes_opt "${MISSING[@]}"; then
        say_ok "安装完成"
        return 0
    fi

    say_err "安装失败"
    echo "  常见原因与处理："
    echo "    - 机器没有联网/没有可用软件源：配置本地源或离线源后重试；"
    echo "      或把对应 .deb 拷到本机后执行 sudo dpkg -i *.deb（注意依赖要一并拷）。"
    echo "    - 没有 --update：软件源索引过旧时可加 --update 重试（需要联网）。"
    return 1
}

# ---------- 主流程 ----------
echo "程序目录：$GAME_DIR"
echo "模式：$MODE"

check_system
check_packages

# 没有明确指定模式（比如双击运行），发现缺少软件包时询问是否现在安装
if [ "$MODE" = "check" ] && [ "$MODE_EXPLICIT" -eq 0 ] && [ "${#MISSING[@]}" -gt 0 ] && [ -t 0 ]; then
    echo
    read -r -p "发现缺少软件包：${MISSING[*]}，是否现在安装？[y/N] " ANSWER || ANSWER=""
    case "$ANSWER" in
        y|Y|yes|YES) MODE="install" ;;
    esac
fi

if [ "$MODE" = "install" ]; then
    if do_install; then
        # 安装后重新检查，确认依赖已补齐（先扣掉第一次检查时记的缺失数，避免重复计数）
        PROBLEMS=$((PROBLEMS - ${#MISSING[@]}))
        check_packages
    fi
fi
check_bundled

title "结果"
if [ "$PROBLEMS" -eq 0 ]; then
    echo "  ${C_OK}所有检查通过${C_END}"
    exit 0
fi

echo "  ${C_ERR}发现 $PROBLEMS 个问题${C_END}"
if [ "$MODE" = "check" ] && [ "${#MISSING[@]}" -gt 0 ]; then
    echo "  可以运行：./install_deps.sh install   安装缺少的软件包"
fi
exit 1
