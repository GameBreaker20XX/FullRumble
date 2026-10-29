#!/bin/sh
# Initializes and updates all third-party submodules (RecompOne, ...).
set -e
git submodule update --init --recursive
echo "update_submodules: done."
