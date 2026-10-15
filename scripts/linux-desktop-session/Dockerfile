FROM mcr.microsoft.com/dotnet/sdk@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS contracts
WORKDIR /source
COPY source.tar /tmp/source.tar
COPY candidate.tar.gz /tmp/candidate.tar.gz
RUN tar -xf /tmp/source.tar && mkdir /candidate && tar -xzf /tmp/candidate.tar.gz --strip-components=1 -C /candidate
RUN dotnet publish tests/linux-desktop/CodexSwitch.LinuxDesktop.Tests.csproj -c Release -r linux-x64 --self-contained true -p:UseSharedCompilation=false -p:CodexSecretDir=/candidate -o /contracts/desktop && dotnet publish tests/linux-vault/CodexSwitch.LinuxVault.Tests.csproj -c Release -r linux-x64 --self-contained true -p:UseSharedCompilation=false -p:CodexSecretDir=/candidate -o /contracts/vault

FROM debian@sha256:913f6706df59a68922d1dd08f78c2476560a8d367897200a6005b00e5f67c2d5
ENV DEBIAN_FRONTEND=noninteractive
COPY candidate.deb upstream.deb /input/
RUN apt-get update -qq && apt-get install --no-install-recommends -y /input/candidate.deb /input/upstream.deb xfce4-session xfwm4 xfce4-panel xfdesktop4 xfce4-settings thunar dbus-x11 libglib2.0-bin gnome-keyring xvfb xauth x11-apps x11-utils x11-xkb-utils xdotool imagemagick python3 python3-pil fonts-dejavu-core fonts-noto-cjk desktop-file-utils procps && rm -rf /var/lib/apt/lists/*
COPY --from=contracts /contracts /contracts
COPY verify-session.py expected.json /work/
RUN useradd --uid 1000 --create-home verifier && chmod 0700 /home/verifier && install -d -m 0700 -o 1000 -g 1000 /results
USER 1000:1000
WORKDIR /work
ENV HOME=/home/verifier CODEX_SWITCH_DISPOSABLE_RUNNER=true DOTNET_CLI_TELEMETRY_OPTOUT=1
CMD ["python3", "/work/verify-session.py"]
