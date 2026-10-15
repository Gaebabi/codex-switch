ARG DESKTOP_IMAGE=codex-switch-desktop-session:verified-local
FROM ${DESKTOP_IMAGE}
USER root
RUN apt-get update -qq && apt-get install --no-install-recommends -y x11vnc novnc websockify firefox-esr python3-gi seahorse && rm -rf /var/lib/apt/lists/*
COPY interactive-session.py /work/interactive-session.py
COPY prepare-keyring.py /work/prepare-keyring.py
USER 1000:1000
CMD ["python3", "/work/interactive-session.py"]
