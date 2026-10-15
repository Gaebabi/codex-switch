ARG BASE_IMAGE
FROM mcr.microsoft.com/dotnet/sdk@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS dotnet
FROM ${BASE_IMAGE}
ENV DEBIAN_FRONTEND=noninteractive DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 PATH=/usr/share/dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
COPY . /verified-input/
RUN apt-get update -qq && apt-get install --no-install-recommends -y python3 sudo ca-certificates dbus-x11 libglib2.0-bin xvfb xauth x11-apps imagemagick libfontconfig1 fonts-dejavu-core fonts-noto-cjk libqca-qt6-plugins && python3 /verified-input/verify-kde6-image.py
ARG TEST_UID
RUN useradd --uid "${TEST_UID}" --create-home verifier && printf 'verifier ALL=(root) NOPASSWD: /usr/bin/chown\n' > /etc/sudoers.d/verifier && chmod 0440 /etc/sudoers.d/verifier
USER verifier
WORKDIR /work
