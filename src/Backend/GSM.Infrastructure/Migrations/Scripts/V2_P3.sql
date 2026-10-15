-- P3: caja por turno. Los pagos existentes no tienen turno (shift_id NULL). La migracion agrega la columna
-- affects_cash_drawer (bool NOT NULL, sin default en el modelo: AddColumn la deja en false); aqui se rellena lo que
-- ya existe: solo el efectivo mueve el cajon.
-- Se ejecuta DESPUES de AddColumn (y de crear payments.cash_shifts / payments.cash_movements).
UPDATE payments.payments SET affects_cash_drawer = (method = 'cash');
