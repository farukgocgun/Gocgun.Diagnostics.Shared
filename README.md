# Gocgun.Diagnostics.Shared

> **Beta.** This package is in beta: the public API may change between releases.

Shared library for `Gocgun.Cache.Ui` and `Gocgun.Bus.Ui`. It holds the password hash, protobuf fallback decoder, `gocgun.bus.v1.Envelope` decode (protobuf-net contracts, no `.proto` files), pattern maps, and the append-only audit log.

MIT (`LICENSE`). This project is not packed. Both diagnostic solutions reference it. It does not reference `Gocgun.Identity`.
