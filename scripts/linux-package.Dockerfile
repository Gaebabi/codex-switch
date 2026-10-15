ARG BASE_IMAGE=debian@sha256:913f6706df59a68922d1dd08f78c2476560a8d367897200a6005b00e5f67c2d5
FROM ${BASE_IMAGE}
ARG PACKAGE_FAMILY=deb
ENV DEBIAN_FRONTEND=noninteractive
COPY . /input/
RUN if [ "$PACKAGE_FAMILY" = deb ]; then apt-get update -qq && apt-get install --no-install-recommends -y python3 sudo ca-certificates dbus-x11 libglib2.0-bin xvfb xauth x11-apps imagemagick fonts-dejavu-core fonts-noto-cjk desktop-file-utils; else dnf install -y python3 sudo shadow-utils dbus-daemon xorg-x11-server-Xvfb xorg-x11-xauth xwd ImageMagick dejavu-sans-fonts google-noto-sans-cjk-fonts desktop-file-utils && dnf clean all; fi
ARG TEST_UID
RUN mkdir /home/verifier && chown "$TEST_UID:$TEST_UID" /home/verifier && if ! getent passwd "$TEST_UID" >/dev/null; then useradd --uid "$TEST_UID" --home-dir /home/verifier verifier; fi && chmod 0700 /home/verifier && python3 /input/verify-package-system.py --prepare "$TEST_UID"
RUN if [ "$PACKAGE_FAMILY" = deb ]; then apt-get install --no-install-recommends -y /input/candidate.deb && printf 'ALL ALL=(root) NOPASSWD: /usr/bin/dpkg --purge codex-switch\n' > /etc/sudoers.d/codex-switch-test; else dnf install -y /input/candidate.rpm && printf 'ALL ALL=(root) NOPASSWD: /usr/bin/rpm -e codex-switch\n' > /etc/sudoers.d/codex-switch-test; fi && chmod 0440 /etc/sudoers.d/codex-switch-test
USER ${TEST_UID}
ENV HOME=/home/verifier CODEX_SWITCH_DISPOSABLE_RUNNER=true
WORKDIR /input
