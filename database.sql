CREATE DATABASE tech_store;
USE tech_store;
CREATE TABLE User (
    user_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255),
    email VARCHAR(255) UNIQUE,
    password_hash VARCHAR(255),
    role ENUM('customer','admin'),
    phone VARCHAR(20),
    address TEXT,
    status VARCHAR(20) DEFAULT 'active', -- Trạng thái tài khoản: active, blocked
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

CREATE TABLE Category (
    category_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255),
    description TEXT
);

CREATE TABLE Product (
    product_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255) NOT NULL, -- Thêm NOT NULL để đảm bảo tên sản phẩm luôn có
    description TEXT,
    price DECIMAL(10,2) NOT NULL, -- Thêm NOT NULL
    original_price DECIMAL(10,2),
    brand VARCHAR(255),
    stock_quantity INT DEFAULT 0, -- Set DEFAULT
    category_id INT NOT NULL, -- Thêm NOT NULL
    image_url VARCHAR(255),
    specifications JSON,
    rating DECIMAL(2,1) DEFAULT 0.0,
    review_count INT DEFAULT 0,
    is_new BOOLEAN DEFAULT FALSE,
    is_best_seller BOOLEAN DEFAULT FALSE,
    
    -- CỘT BỔ SUNG CHO CHATBOT AI
    use_case TEXT,             -- Mô tả ngữ cảnh sử dụng (Cho ai? Làm gì?)
    usp TEXT,                  -- Điểm bán hàng độc đáo (Unique Selling Points)
    warranty_period VARCHAR(50), -- Thời gian bảo hành (Ví dụ: '12 tháng')
    return_policy_days INT,    -- Số ngày đổi trả (Ví dụ: 7)
    
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (category_id) REFERENCES Category(category_id)
);

CREATE TABLE Cart (
    cart_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT,
    FOREIGN KEY (user_id) REFERENCES User(user_id)
);

CREATE TABLE CartItem (
    cart_item_id INT AUTO_INCREMENT PRIMARY KEY,
    cart_id INT,
    product_id INT,
    quantity INT,
    FOREIGN KEY (cart_id) REFERENCES Cart(cart_id),
    FOREIGN KEY (product_id) REFERENCES Product(product_id)
);

CREATE TABLE OrderTable (
    order_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT,
    order_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    status ENUM('pending', 'shipped', 'completed', 'canceled'),
    total_amount DECIMAL(10,2),
    shipping_address TEXT,
    payment_status ENUM('unpaid', 'paid'),
    payment_method VARCHAR(50),
    FOREIGN KEY (user_id) REFERENCES User(user_id)
);

CREATE TABLE OrderItem (
    order_item_id INT AUTO_INCREMENT PRIMARY KEY,
    order_id INT,
    product_id INT,
    quantity INT,
    price DECIMAL(10,2),
    FOREIGN KEY (order_id) REFERENCES OrderTable(order_id),
    FOREIGN KEY (product_id) REFERENCES Product(product_id)
);

CREATE TABLE Review (
    review_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT,
    product_id INT,
    order_item_id INT,
    rating INT CHECK (rating BETWEEN 1 AND 5),
    comment TEXT,
    is_verified BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES User(user_id),
    FOREIGN KEY (product_id) REFERENCES Product(product_id),
    FOREIGN KEY (order_item_id) REFERENCES OrderItem(order_item_id),
    UNIQUE KEY unique_review_per_order_item (order_item_id),
    INDEX idx_product_rating (product_id, rating),
    INDEX idx_user_reviews (user_id, created_at)
);

-- Trigger để tự động verify review khi order đã completed
DELIMITER //
CREATE TRIGGER verify_review_after_insert
    AFTER INSERT ON Review
    FOR EACH ROW
BEGIN
    DECLARE order_status VARCHAR(20);
    
    SELECT ot.status INTO order_status
    FROM OrderTable ot
    JOIN OrderItem oi ON ot.order_id = oi.order_id
    WHERE oi.order_item_id = NEW.order_item_id;
    
    IF order_status = 'completed' THEN
        UPDATE Review 
        SET is_verified = TRUE 
        WHERE review_id = NEW.review_id;
    END IF;
END//
DELIMITER ;

-- Trigger để verify review khi order status thay đổi thành completed
DELIMITER //
CREATE TRIGGER verify_reviews_on_order_complete
    AFTER UPDATE ON OrderTable
    FOR EACH ROW
BEGIN
    IF NEW.status = 'completed' AND OLD.status != 'completed' THEN
        UPDATE Review r
        JOIN OrderItem oi ON r.order_item_id = oi.order_item_id
        SET r.is_verified = TRUE
        WHERE oi.order_id = NEW.order_id;
    END IF;
END//
DELIMITER ;

-- Trigger để cập nhật rating và review_count khi thêm review mới
DELIMITER //
CREATE TRIGGER update_product_rating_after_insert
    AFTER INSERT ON Review
    FOR EACH ROW
BEGIN
    UPDATE Product 
    SET 
        rating = (
            SELECT ROUND(AVG(rating), 1) 
            FROM Review 
            WHERE product_id = NEW.product_id AND is_verified = TRUE
        ),
        review_count = (
            SELECT COUNT(*) 
            FROM Review 
            WHERE product_id = NEW.product_id AND is_verified = TRUE
        )
    WHERE product_id = NEW.product_id;
END//
DELIMITER ;

-- Trigger để cập nhật rating và review_count khi cập nhật review
DELIMITER //
CREATE TRIGGER update_product_rating_after_update
    AFTER UPDATE ON Review
    FOR EACH ROW
BEGIN
    -- Cập nhật cho product của review cũ (nếu product_id thay đổi)
    IF OLD.product_id != NEW.product_id THEN
        UPDATE Product 
        SET 
            rating = (
                SELECT COALESCE(ROUND(AVG(rating), 1), 0) 
                FROM Review 
                WHERE product_id = OLD.product_id AND is_verified = TRUE
            ),
            review_count = (
                SELECT COUNT(*) 
                FROM Review 
                WHERE product_id = OLD.product_id AND is_verified = TRUE
            )
        WHERE product_id = OLD.product_id;
    END IF;
    
    -- Cập nhật cho product của review mới
    UPDATE Product 
    SET 
        rating = (
            SELECT COALESCE(ROUND(AVG(rating), 1), 0) 
            FROM Review 
            WHERE product_id = NEW.product_id AND is_verified = TRUE
        ),
        review_count = (
            SELECT COUNT(*) 
            FROM Review 
            WHERE product_id = NEW.product_id AND is_verified = TRUE
        )
    WHERE product_id = NEW.product_id;
END//
DELIMITER ;

-- Trigger để cập nhật rating và review_count khi xóa review
DELIMITER //
CREATE TRIGGER update_product_rating_after_delete
    AFTER DELETE ON Review
    FOR EACH ROW
BEGIN
    UPDATE Product 
    SET 
        rating = (
            SELECT COALESCE(ROUND(AVG(rating), 1), 0) 
            FROM Review 
            WHERE product_id = OLD.product_id AND is_verified = TRUE
        ),
        review_count = (
            SELECT COUNT(*) 
            FROM Review 
            WHERE product_id = OLD.product_id AND is_verified = TRUE
        )
    WHERE product_id = OLD.product_id;
END//
DELIMITER ;
-- Thêm tài khoản admin mặc định

INSERT INTO User (name, email, password_hash, role, phone, address, status)
VALUES ('Administrator', 'admin@example.com', '$2a$11$LW/kugJ.FEoHUCLP0KASp.umaQEFQYJjTxQFP5XNx//3eUVmStvXi', 'admin', '0123456789', 'Hà Nội, Việt Nam', 'active'),
       ('Nguyen Van B', 'customer@example.com', '$2a$11$LW/kugJ.FEoHUCLP0KASp.umaQEFQYJjTxQFP5XNx//3eUVmStvXi', 'customer', '0987654321', 'TP.HCM, Việt Nam', 'active');

INSERT INTO Category (name, description) VALUES
('Điện thoại', 'Các loại điện thoại thông minh, điện thoại phổ thông'),
('Laptop', 'Các loại máy tính xách tay, laptop gaming, laptop văn phòng'),
('Phụ kiện', 'Tai nghe, sạc, cáp, ốp lưng, chuột máy tính'),
('Máy tính bảng','Các loại máy tính bảng chính hãng, hiệu năng cao');
INSERT INTO Product (name, description, price, original_price, brand, stock_quantity, category_id, image_url, specifications, rating, review_count, is_new, is_best_seller) VALUES
-- Điện thoại
('iPhone 15 128GB', 'iPhone 15 chính hãng VN/A, màu đen', 25000000, 27000000, 'Apple', 50, 1, 'https://example.com/images/iphone15.jpg', 
 '{"screen": "6.1 inch Super Retina XDR", "chip": "A17 Bionic", "ram": "6GB", "storage": "128GB", "camera": "48MP + 12MP", "battery": "3349mAh"}', 
 4.5, 128, TRUE, TRUE),

('Samsung Galaxy S24', 'Samsung Galaxy S24 chính hãng, màu trắng', 22000000, 24000000, 'Samsung', 40, 1, 'https://example.com/images/galaxy_s24.jpg',
 '{"screen": "6.2 inch Dynamic AMOLED 2X", "chip": "Exynos 2400", "ram": "8GB", "storage": "256GB", "camera": "50MP + 12MP + 10MP", "battery": "4000mAh"}',
 4.3, 95, TRUE, FALSE),

('Xiaomi Redmi Note 13', 'Redmi Note 13 5G chính hãng, màu xanh', 7000000, 8000000, 'Xiaomi', 60, 1, 'https://example.com/images/redmi_note13.jpg',
 '{"screen": "6.67 inch AMOLED", "chip": "Snapdragon 7s Gen 2", "ram": "8GB", "storage": "128GB", "camera": "108MP + 8MP + 2MP", "battery": "5000mAh"}',
 4.2, 67, FALSE, TRUE),

-- Laptop
('MacBook Air M3 2025', 'MacBook Air M3 8GB/256GB, màu bạc', 32000000, 35000000, 'Apple', 30, 2, 'https://example.com/images/macbook_air_m3.jpg',
 '{"screen": "13.6 inch Liquid Retina", "chip": "Apple M3", "ram": "8GB", "storage": "256GB SSD", "graphics": "8-core GPU", "weight": "1.24kg"}',
 4.6, 89, TRUE, TRUE),

('Dell XPS 13', 'Dell XPS 13 2025, i7, 16GB RAM, 512GB SSD', 35000000, 38000000, 'Dell', 25, 2, 'https://example.com/images/dell_xps_13.jpg',
 '{"screen": "13.4 inch InfinityEdge", "cpu": "Intel Core i7-1365U", "ram": "16GB LPDDR5", "storage": "512GB SSD", "graphics": "Intel Iris Xe", "weight": "1.19kg"}',
 4.4, 56, FALSE, FALSE),

('ASUS ROG Zephyrus G14', 'Laptop gaming ASUS ROG G14, Ryzen 9, 32GB RAM', 42000000, 45000000, 'ASUS', 20, 2, 'https://example.com/images/asus_rog_g14.jpg',
 '{"screen": "14 inch QHD 165Hz", "cpu": "AMD Ryzen 9 7940HS", "ram": "32GB DDR5", "storage": "1TB SSD", "graphics": "RTX 4070", "weight": "1.65kg"}',
 4.7, 43, FALSE, TRUE),

-- Phụ kiện
('Tai nghe AirPods Pro 2', 'Tai nghe AirPods Pro 2 chính hãng Apple', 5200000, 5500000, 'Apple', 80, 3, 'https://example.com/images/airpods_pro_2.jpg',
 '{"type": "In-ear", "connection": "Bluetooth 5.3", "anc": "Active Noise Cancellation", "battery": "6h + 24h with case", "features": "Spatial Audio, Transparency Mode"}',
 4.5, 234, FALSE, TRUE),

('Sạc nhanh Anker 65W', 'Sạc nhanh Anker 65W PowerPort, sạc laptop & điện thoại', 850000, 950000, 'Anker', 100, 3, 'https://example.com/images/anker_65w.jpg',
 '{"power": "65W", "ports": "USB-C x2, USB-A x1", "tech": "PowerIQ 3.0", "size": "70 x 70 x 32mm", "weight": "130g"}',
 4.6, 187, FALSE, FALSE),

('Chuột Logitech MX Master 3', 'Chuột không dây Logitech MX Master 3', 2500000, 2800000, 'Logitech', 50, 3, 'https://example.com/images/logitech_mx_master_3.jpg',
 '{"type": "Wireless", "connection": "Bluetooth + USB Receiver", "dpi": "4000 DPI", "battery": "70 days", "features": "MagSpeed scroll, Multi-device"}',
 4.4, 156, FALSE, FALSE);

-- ALTER TABLE Product ADD COLUMN use_case TEXT, ADD COLUMN usp TEXT, ADD COLUMN warranty_period VARCHAR(50), ADD COLUMN return_policy_days INT;

-- ====================================================
-- 1. Điện thoại
-- ====================================================

UPDATE Product
SET
    use_case = 'Lý tưởng cho người dùng hệ sinh thái Apple, cần hiệu năng ổn định, camera chất lượng cao và thiết kế bền bỉ. Phù hợp cho công việc văn phòng và giải trí cơ bản.',
    usp = 'Chip A17 Bionic mạnh mẽ, Camera 48MP Pro, Cổng USB-C tiện lợi.',
    warranty_period = '12 tháng chính hãng',
    return_policy_days = 14
WHERE name = 'iPhone 15 128GB';

UPDATE Product
SET
    use_case = 'Dành cho người dùng Android cao cấp, yêu thích màn hình Dynamic AMOLED và các tính năng AI mới nhất của Samsung. Phù hợp cho đa nhiệm và sáng tạo nội dung.',
    usp = 'Tích hợp Galaxy AI (Phiên dịch trực tiếp), Màn hình Dynamic AMOLED 2X, Chip Exynos 2400 hiệu năng cao.',
    warranty_period = '12 tháng chính hãng',
    return_policy_days = 14
WHERE name = 'Samsung Galaxy S24';

UPDATE Product
SET
    use_case = 'Lựa chọn tầm trung cho sinh viên hoặc người dùng phổ thông, tập trung vào thời lượng pin dài, sạc nhanh và camera độ phân giải cao.',
    usp = 'Camera chính 108MP, Pin 5000mAh, Màn hình AMOLED 120Hz mượt mà.',
    warranty_period = '18 tháng chính hãng',
    return_policy_days = 7
WHERE name = 'Xiaomi Redmi Note 13';

-- ====================================================
-- 2. Laptop
-- ====================================================

UPDATE Product
SET
    use_case = 'Phù hợp cho sinh viên, dân văn phòng, và người sáng tạo nội dung cơ bản. Cần máy tính mỏng nhẹ, pin cực lâu và xử lý tác vụ yên tĩnh.',
    usp = 'Chip Apple M3 hiệu năng cao, Thiết kế không quạt (yên tĩnh), Thời lượng pin lên đến 18 giờ.',
    warranty_period = '12 tháng chính hãng',
    return_policy_days = 14
WHERE name = 'MacBook Air M3 2025';

UPDATE Product
SET
    use_case = 'Dành cho doanh nhân và các chuyên gia cần một chiếc laptop Windows mỏng nhẹ, thiết kế cao cấp và hiệu suất mạnh mẽ cho công việc văn phòng chuyên sâu.',
    usp = 'Thiết kế InfinityEdge (viền siêu mỏng), Chất liệu nhôm nguyên khối, Hiệu năng Intel Core i7 mạnh mẽ.',
    warranty_period = '12 tháng chính hãng',
    return_policy_days = 7
WHERE name = 'Dell XPS 13';

UPDATE Product
SET
    use_case = 'Laptop gaming di động, dành cho game thủ và các nhà thiết kế/editor video cần card đồ họa mạnh mẽ trong một thân hình nhỏ gọn.',
    usp = 'Card đồ họa RTX 4070, CPU AMD Ryzen 9 hiệu suất cao, Màn hình 14 inch QHD 165Hz.',
    warranty_period = '24 tháng chính hãng',
    return_policy_days = 7
WHERE name = 'ASUS ROG Zephyrus G14';

-- ====================================================
-- 3. Phụ kiện
-- ====================================================

UPDATE Product
SET
    use_case = 'Lý tưởng cho người dùng Apple muốn trải nghiệm âm thanh chất lượng cao, chống ồn chủ động hiệu quả, và tích hợp sâu với các thiết bị iOS/macOS.',
    usp = 'Chống ồn chủ động (ANC) hàng đầu, Chế độ xuyên âm (Transparency Mode), Spatial Audio.',
    warranty_period = '12 tháng chính hãng',
    return_policy_days = 14
WHERE name = 'Tai nghe AirPods Pro 2';

UPDATE Product
SET
    use_case = 'Củ sạc đa năng, phù hợp cho người thường xuyên di chuyển cần sạc cả laptop và điện thoại một cách nhanh chóng chỉ với một thiết bị.',
    usp = 'Công suất 65W sạc laptop, 3 cổng (USB-C và USB-A), Công nghệ PowerIQ 3.0 sạc nhanh và an toàn.',
    warranty_period = '18 tháng chính hãng',
    return_policy_days = 7
WHERE name = 'Sạc nhanh Anker 65W';

UPDATE Product
SET
    use_case = 'Chuột công thái học dành cho lập trình viên, designer, và người làm văn phòng cần độ chính xác cao và làm việc liên tục trong thời gian dài.',
    usp = 'Thiết kế công thái học (Ergonomic), Cuộn MagSpeed siêu nhanh, Kết nối Multi-device.',
    warranty_period = '12 tháng chính hãng',
    return_policy_days = 7
WHERE name = 'Chuột Logitech MX Master 3';
INSERT INTO Product (
    name, description, price, original_price, brand, stock_quantity, category_id, image_url, specifications, rating, review_count, is_new, is_best_seller, 
    use_case, usp, warranty_period, return_policy_days
) 
VALUES 
-- ====================================================
-- Danh mục 1: Điện thoại (Thêm 7 sản phẩm mới -> Tổng 10)
-- ====================================================
('iPhone 15 Pro Max 256GB', 'Siêu phẩm iPhone 15 Pro Max, khung Titan, camera 48MP', 32000000, 34000000, 'Apple', 30, 1, '/images/iphone15-pro-max.jpg', 
    '{"screen": "6.7 inch Super Retina XDR ProMotion 120Hz", "chip": "A17 Pro", "ram": "8GB", "storage": "256GB", "camera": "48MP+12MP+12MP", "battery": "4422mAh"}', 
    4.8, 210, TRUE, TRUE, 
    'Phù hợp người dùng chuyên nghiệp, quay phim 4K, cần hiệu năng cao nhất.', 'Chip A17 Pro, Khung Titan, Camera Tele 5x.', '12 tháng chính hãng', 14),

('Samsung Galaxy Z Flip 5 256GB', 'Điện thoại gập thời trang, màn hình Flex Window lớn', 19000000, 22000000, 'Samsung', 25, 1, '/images/galaxy-zflip5.jpg', 
    '{"screen": "6.7 inch Dynamic AMOLED 2X", "chip": "Snapdragon 8 Gen 2 for Galaxy", "ram": "8GB", "storage": "256GB", "camera": "12MP+12MP", "battery": "3700mAh"}', 
    4.5, 120, TRUE, FALSE, 
    'Thời trang, người dùng yêu thích sự nhỏ gọn, chụp ảnh selfie linh hoạt.', 'Màn hình ngoài lớn (Flex Window), Gập mở nhỏ gọn, Camera FlexCam.', '12 tháng chính hãng', 14),

('Google Pixel 8 128GB', 'Thuần Android, tích hợp AI sâu từ Google, camera chân thực.', 15000000, 17000000, 'Google', 20, 1, '/images/pixel-8.jpg', 
    '{"screen": "6.2 inch OLED", "chip": "Google Tensor G3", "ram": "8GB", "storage": "128GB", "camera": "50MP+12MP", "battery": "4575mAh"}', 
    4.7, 90, TRUE, FALSE, 
    'Thuần Android, người thích trải nghiệm AI tích hợp sâu từ Google và chụp ảnh chân thực.', 'Thuần Android, Camera AI tốt nhất phân khúc, Tính năng Magic Eraser.', '12 tháng chính hãng', 7),

('OPPO Reno 11 5G 256GB', 'Thiết kế đẹp, camera chụp chân dung, sạc siêu nhanh.', 10000000, 11500000, 'OPPO', 50, 1, '/images/oppo-reno11.jpg', 
    '{"screen": "6.7 inch AMOLED 120Hz", "chip": "MediaTek Dimensity 7050", "ram": "8GB", "storage": "256GB", "camera": "50MP+8MP+32MP", "battery": "4800mAh"}', 
    4.3, 80, TRUE, TRUE, 
    'Nhu cầu tầm trung, thích thiết kế đẹp, camera chụp chân dung và sạc siêu nhanh.', 'Thiết kế mỏng nhẹ, Camera chân dung 32MP, Sạc nhanh 67W.', '12 tháng chính hãng', 7),

('Realme C55 64GB', 'Ngân sách thấp, pin trâu và màn hình lớn.', 4000000, 4500000, 'Realme', 70, 1, '/images/realme-c55.jpg', 
    '{"screen": "6.72 inch IPS LCD 90Hz", "chip": "MediaTek Helio G88", "ram": "6GB", "storage": "64GB", "camera": "64MP", "battery": "5000mAh"}', 
    4.0, 150, FALSE, TRUE, 
    'Học sinh, sinh viên, ngân sách thấp, cần pin trâu và màn hình lớn.', 'Pin 5000mAh, Màn hình 90Hz, Thiết kế Mini Capsule.', '12 tháng chính hãng', 7),

('Samsung Galaxy A54 5G', 'Điện thoại tầm trung, kháng nước IP67, màn hình Super AMOLED.', 8500000, 9500000, 'Samsung', 45, 1, '/images/galaxy-a54.jpg', 
    '{"screen": "6.4 inch Super AMOLED 120Hz", "chip": "Exynos 1380", "ram": "8GB", "storage": "128GB", "camera": "50MP+12MP+5MP", "battery": "5000mAh"}', 
    4.4, 110, FALSE, FALSE, 
    'Người dùng tầm trung cần điện thoại bền bỉ, kháng nước và màn hình hiển thị đẹp.', 'Kháng nước IP67, Màn hình Super AMOLED, Pin 2 ngày.', '12 tháng chính hãng', 14),

('Xiaomi 13T Pro 512GB', 'Flagship Killer, hiệu năng mạnh, camera Leica chuyên nghiệp.', 16000000, 18000000, 'Xiaomi', 25, 1, '/images/xiaomi-13t-pro.jpg', 
    '{"screen": "6.67 inch AMOLED 144Hz", "chip": "Dimensity 9200+", "ram": "12GB", "storage": "512GB", "camera": "50MP+12MP+50MP", "battery": "5000mAh"}', 
    4.6, 70, TRUE, TRUE, 
    'Game thủ và nhiếp ảnh gia cần hiệu năng cao nhất và chất lượng ảnh Leica ở phân khúc cận cao cấp.', 'Sạc siêu nhanh 120W (đầy pin 19 phút), Camera Leica, Màn hình 144Hz.', '24 tháng chính hãng', 7),


-- ====================================================
-- Danh mục 2: Laptop (Thêm 7 sản phẩm mới -> Tổng 10)
-- ====================================================
('MacBook Pro M3 Pro 14 inch', 'Laptop cho chuyên gia sáng tạo, hiệu năng cực mạnh.', 45000000, 48000000, 'Apple', 15, 2, '/images/macbook-pro-m3pro.jpg', 
    '{"screen": "14.2 inch Liquid Retina XDR", "chip": "Apple M3 Pro", "ram": "18GB", "storage": "512GB SSD", "graphics": "14-core GPU", "weight": "1.6kg"}', 
    4.9, 80, TRUE, TRUE, 
    'Dành cho Editor video, thiết kế đồ họa 3D, lập trình viên chuyên nghiệp.', 'Chip M3 Pro hiệu năng đột phá, Màn hình Liquid Retina XDR, Thời lượng pin 22 giờ.', '12 tháng chính hãng', 14),

('Lenovo Legion Pro 5 Gen 8', 'Laptop gaming hiệu năng khủng, màn hình tốc độ cao.', 38000000, 41000000, 'Lenovo', 18, 2, '/images/lenovo-legion-5.jpg', 
    '{"screen": "16 inch QHD 240Hz", "cpu": "Intel Core i7-13700HX", "ram": "32GB DDR5", "storage": "1TB SSD", "graphics": "NVIDIA RTX 4070 8GB", "weight": "2.5kg"}', 
    4.7, 50, FALSE, TRUE, 
    'Game thủ hard-core, cần hiệu năng gaming và tản nhiệt mạnh mẽ.', 'Card đồ họa RTX 4070, Màn hình 240Hz, Tản nhiệt hiệu quả.', '24 tháng chính hãng', 7),

('HP Spectre x360 14', 'Laptop 2-trong-1 cao cấp, thiết kế xoay gập linh hoạt.', 36000000, 39000000, 'HP', 12, 2, '/images/hp-spectre-x360.jpg', 
    '{"screen": "13.5 inch 3K2K OLED Touch", "cpu": "Intel Core i7-1355U", "ram": "16GB LPDDR5", "storage": "1TB SSD", "graphics": "Intel Iris Xe", "weight": "1.36kg"}', 
    4.6, 40, TRUE, FALSE, 
    'Doanh nhân, người thường xuyên trình bày, cần máy 2-trong-1 cao cấp, cảm ứng.', 'Thiết kế xoay 360 độ, Màn hình OLED, Bút stylus kèm theo.', '12 tháng chính hãng', 7),

('Microsoft Surface Laptop 5', 'Laptop sang trọng, tối ưu cho Microsoft Office.', 28000000, 30000000, 'Microsoft', 20, 2, '/images/surface-laptop-5.jpg', 
    '{"screen": "13.5 inch PixelSense Touch", "cpu": "Intel Core i5-1235U", "ram": "8GB LPDDR5", "storage": "512GB SSD", "graphics": "Intel Iris Xe", "weight": "1.27kg"}', 
    4.5, 35, FALSE, FALSE, 
    'Người dùng cần trải nghiệm Windows mượt mà, thiết kế sang trọng, tối ưu cho Microsoft Office.', 'Màn hình cảm ứng PixelSense, Vật liệu Alcantara, Trải nghiệm Windows thuần túy.', '12 tháng chính hãng', 14),

('ASUS Zenbook 14 OLED UX3405', 'Mỏng nhẹ, màn hình OLED đẹp, pin trâu.', 22000000, 24000000, 'ASUS', 28, 2, '/images/asus-zenbook-14.jpg', 
    '{"screen": "14 inch 3K OLED 120Hz", "cpu": "Intel Core Ultra 7", "ram": "16GB LPDDR5", "storage": "512GB SSD", "graphics": "Intel Arc", "weight": "1.2kg"}', 
    4.7, 48, TRUE, TRUE, 
    'Văn phòng, sinh viên, cần máy mỏng nhẹ, pin tốt, màn hình đẹp với mức giá hợp lý.', 'Màn hình OLED 3K 120Hz, Siêu mỏng nhẹ (1.2kg), Pin dài.', '24 tháng chính hãng', 7),

('Dell Inspiron 15', 'Laptop phổ thông, màn hình lớn 15.6 inch.', 13000000, 15000000, 'Dell', 40, 2, '/images/dell-inspiron-15.jpg', 
    '{"screen": "15.6 inch Full HD", "cpu": "Intel Core i5-1235U", "ram": "8GB DDR4", "storage": "256GB SSD", "graphics": "Intel Iris Xe", "weight": "1.8kg"}', 
    4.2, 60, FALSE, FALSE, 
    'Sử dụng tại nhà, học tập online, nhu cầu cơ bản với màn hình lớn.', 'Màn hình lớn 15.6 inch, Bàn phím số đầy đủ, Giá thành hợp lý.', '12 tháng chính hãng', 7),

('Acer Swift Go 14 OLED', 'Laptop tầm trung có màn hình OLED, mỏng nhẹ.', 18000000, 20000000, 'Acer', 35, 2, '/images/acer-swift-go-14.jpg', 
    '{"screen": "14 inch 2.8K OLED", "cpu": "Intel Core i5-13500H", "ram": "16GB LPDDR5", "storage": "512GB SSD", "graphics": "Intel Iris Xe", "weight": "1.3kg"}', 
    4.4, 30, TRUE, FALSE, 
    'Sinh viên ngành đồ họa cơ bản và người làm văn phòng cần màu sắc chính xác, chất lượng hiển thị cao.', 'Màn hình OLED 2.8K, Thiết kế vỏ nhôm, Webcam QHD.', '24 tháng chính hãng', 7),

-- ====================================================
-- Danh mục 3: Phụ kiện (Thêm 7 sản phẩm mới -> Tổng 10)
-- ====================================================
('Bàn phím Logitech MX Keys S', 'Bàn phím không dây cao cấp, gõ êm, đèn nền thông minh.', 2800000, 3200000, 'Logitech', 40, 3, '/images/logitech-mxkeys-s.jpg', 
    '{"type": "Wireless", "connection": "Bluetooth + Bolt Receiver", "battery": "10 ngày", "features": "Perfect Stroke Keys, Sạc USB-C, Smart Illumination"}', 
    4.8, 80, TRUE, TRUE, 
    'Lập trình viên, người gõ văn bản nhiều, cần trải nghiệm gõ phím yên tĩnh, chính xác.', 'Công nghệ gõ Perfect Stroke, Nút tắt tiếng (Mute), Kết nối 3 thiết bị.', '12 tháng chính hãng', 7),

('Ổ cứng SSD Samsung T7 1TB', 'Ổ cứng di động tốc độ cao, bền bỉ, bảo mật vân tay.', 3500000, 4000000, 'Samsung', 50, 3, '/images/samsung-ssd-t7.jpg', 
    '{"capacity": "1TB", "connection": "USB 3.2 Gen 2", "speed": "1050MB/s", "features": "Chống sốc, Bảo mật vân tay"}', 
    4.9, 150, FALSE, TRUE, 
    'Chuyên gia cần lưu trữ dữ liệu lớn, tốc độ truyền tải cực nhanh cho video 4K.', 'Tốc độ đọc/ghi 1050MB/s, Bảo mật bằng vân tay, Chống sốc.', '36 tháng chính hãng', 30),

('Giá đỡ laptop Nillkin Pro', 'Giá đỡ công thái học, chất liệu hợp kim nhôm, tản nhiệt tốt.', 550000, 700000, 'Nillkin', 80, 3, '/images/nillkin-laptop-stand.jpg', 
    '{"material": "Hợp kim nhôm", "adjustable_levels": "7 cấp độ", "compatible_size": "11-17 inch", "weight": "250g"}', 
    4.6, 200, FALSE, FALSE, 
    'Người làm việc cố định, cần cải thiện tư thế ngồi, tản nhiệt cho laptop.', 'Thiết kế công thái học (Ergonomic), Chất liệu hợp kim nhôm, Gấp gọn di động.', '6 tháng chính hãng', 7),

('Cáp sạc Innostyle C-L 2m', 'Cáp sạc Lightning bọc dù chống đứt, hỗ trợ sạc nhanh.', 300000, 400000, 'Innostyle', 120, 3, '/images/innostyle-cable.jpg', 
    '{"type": "USB-C to Lightning", "length": "2m", "power": "Hỗ trợ 27W", "features": "MFi Certified, Bọc dù Kevlar"}', 
    4.7, 90, FALSE, FALSE, 
    'Người cần sạc nhanh iPhone/iPad và muốn cáp sạc có độ bền cao, chống đứt gãy.', 'Chứng nhận MFi của Apple, Bọc dù Kevlar chống đứt, Hỗ trợ sạc nhanh PD.', '12 tháng chính hãng', 7),

('Webcam Logitech C920S Pro', 'Webcam Full HD chuyên nghiệp, tích hợp màn trập riêng tư.', 1500000, 1800000, 'Logitech', 35, 3, '/images/logitech-c920s.jpg', 
    '{"resolution": "1080p/30fps", "focus": "Tự động", "mic": "Mic kép Stereo", "features": "Privacy Shutter"}', 
    4.5, 60, FALSE, FALSE, 
    'Họp online, livestream cơ bản, cần hình ảnh Full HD rõ nét và bảo mật riêng tư.', 'Độ phân giải Full HD 1080p, Tích hợp màn trập riêng tư (Privacy Shutter), Mic kép.', '12 tháng chính hãng', 7),

('Pin dự phòng Anker PowerCore III Sense 10000mAh', 'Pin dự phòng mỏng nhẹ, hỗ trợ sạc nhanh PD.', 750000, 900000, 'Anker', 150, 3, '/images/anker-powercore-10k.jpg', 
    '{"capacity": "10000mAh", "ports": "USB-C, USB-A", "tech": "Power Delivery (PD), PowerIQ", "weight": "200g"}', 
    4.6, 250, FALSE, TRUE, 
    'Người dùng thường xuyên ra ngoài, cần nguồn điện dự phòng mỏng nhẹ, hỗ trợ sạc nhanh cho điện thoại.', 'Hỗ trợ sạc nhanh PD 20W, Thiết kế mỏng, bề mặt vải.', '18 tháng chính hãng', 7),

('Loa di động Sony SRS-XB13', 'Loa Bluetooth nhỏ gọn, chống nước, âm thanh Extra Bass.', 990000, 1200000, 'Sony', 60, 3, '/images/sony-srs-xb13.jpg', 
    '{"type": "Bluetooth Speaker", "waterproof": "IP67", "battery": "16 giờ", "features": "Extra Bass, Tích hợp micro"}', 
    4.4, 75, FALSE, FALSE, 
    'Người yêu thích âm nhạc, cần loa di động nhỏ gọn, có thể mang đi biển/hồ bơi (chống nước).', 'Chống nước/bụi IP67, Công nghệ Extra Bass, Thời lượng pin 16 giờ.', '12 tháng chính hãng', 14),

-- ====================================================
-- Danh mục 4: Máy tính bảng (Thêm 10 sản phẩm mới -> Tổng 10)
-- ====================================================
('iPad Air M1 (256GB WiFi)', 'iPad Air với chip M1 mạnh mẽ, màn hình Liquid Retina.', 18000000, 20000000, 'Apple', 22, 4, '/images/ipad-air-m1.jpg', 
    '{"screen": "10.9 inch Liquid Retina", "chip": "Apple M1", "ram": "8GB", "storage": "256GB", "features": "Hỗ trợ Apple Pencil 2", "weight": "461g"}', 
    4.8, 85, TRUE, TRUE, 
    'Học sinh, sinh viên, người dùng sáng tạo cần hiệu năng mạnh mẽ trong tầm giá phải chăng.', 'Chip M1 mạnh mẽ, Màn hình Liquid Retina, Hỗ trợ Apple Pencil 2.', '12 tháng chính hãng', 14),

('Samsung Galaxy Tab S9 Ultra 512GB', 'Máy tính bảng màn hình lớn nhất, hiệu năng cao.', 30000000, 32000000, 'Samsung', 18, 4, '/images/galaxy-tab-s9-ultra.jpg', 
    '{"screen": "14.6 inch Dynamic AMOLED 2X", "chip": "Snapdragon 8 Gen 2 for Galaxy", "ram": "12GB", "storage": "512GB", "features": "Kèm bút S Pen, IP68", "weight": "732g"}', 
    4.9, 50, TRUE, FALSE, 
    'Người dùng muốn thay thế laptop, cần màn hình lớn nhất, hiệu năng cao nhất trên Android.', 'Màn hình Dynamic AMOLED 14.6 inch, Kèm bút S Pen, Chống nước IP68.', '12 tháng chính hãng', 14),

('Xiaomi Pad 5 128GB', 'Máy tính bảng tầm trung, màn hình 120Hz, giải trí tốt.', 8500000, 9500000, 'Xiaomi', 40, 4, '/images/xiaomi-pad-5.jpg', 
    '{"screen": "11 inch IPS 120Hz", "chip": "Snapdragon 860", "ram": "6GB", "storage": "128GB", "features": "4 loa Dolby Atmos", "battery": "8720mAh"}', 
    4.4, 130, FALSE, TRUE, 
    'Giải trí, xem phim, chơi game, ngân sách tầm trung, cần màn hình 120Hz mượt mà.', 'Màn hình 120Hz, 4 loa Dolby Atmos, Chip Snapdragon 860.', '12 tháng chính hãng', 7),

('Lenovo Tab P11 Pro Gen 2', 'Máy tính bảng làm việc, màn hình OLED 120Hz cao cấp.', 14000000, 16000000, 'Lenovo', 15, 4, '/images/lenovo-tab-p11-pro.jpg', 
    '{"screen": "11.2 inch OLED 120Hz", "chip": "MediaTek Kompanio 1300T", "ram": "8GB", "storage": "256GB", "features": "Hỗ trợ chế độ Desktop", "battery": "8000mAh"}', 
    4.6, 45, TRUE, FALSE, 
    'Người dùng cần máy tính bảng làm việc, màn hình OLED đẹp, có thể dùng làm màn hình thứ hai cho PC.', 'Màn hình OLED 120Hz, Hỗ trợ chế độ Desktop, Hiệu năng tốt.', '12 tháng chính hãng', 7),

('Huawei MatePad 11', 'Máy tính bảng học tập, hệ sinh thái HarmonyOS.', 10000000, 11000000, 'Huawei', 30, 4, '/images/huawei-matepad-11.jpg', 
    '{"screen": "10.95 inch IPS 120Hz", "chip": "Snapdragon 865", "ram": "6GB", "storage": "128GB", "features": "Hỗ trợ M-Pencil", "battery": "7250mAh"}', 
    4.3, 50, FALSE, FALSE, 
    'Học sinh, sinh viên cần máy tính bảng để học tập, ghi chú, và hệ sinh thái HarmonyOS.', 'Màn hình 120Hz, Hỗ trợ bút Huawei M-Pencil, Tối ưu cho học tập.', '12 tháng chính hãng', 7),

('Samsung Galaxy Tab A9+ (Plus)', 'Máy tính bảng phổ thông, giải trí cơ bản.', 6000000, 7000000, 'Samsung', 55, 4, '/images/galaxy-tab-a9-plus.jpg', 
    '{"screen": "11 inch LCD 90Hz", "chip": "Snapdragon 695", "ram": "4GB", "storage": "64GB", "features": "Loa Quad Speakers", "battery": "7040mAh"}', 
    4.2, 70, FALSE, TRUE, 
    'Nhu cầu giải trí cơ bản, xem phim, lướt web, học tập online với mức giá thấp.', 'Màn hình 11 inch 90Hz, Loa Quad Speakers, Thiết kế kim loại.', '12 tháng chính hãng', 7),

('iPad 10.9 inch (Gen 10)', 'Máy tính bảng cơ bản của Apple, chip A14 Bionic.', 12500000, 14000000, 'Apple', 28, 4, '/images/ipad-gen10.jpg', 
    '{"screen": "10.9 inch Liquid Retina", "chip": "A14 Bionic", "ram": "4GB", "storage": "64GB", "features": "Hỗ trợ Magic Keyboard Folio", "weight": "477g"}', 
    4.7, 60, FALSE, FALSE, 
    'Người dùng Apple cần máy tính bảng để học tập, giải trí và không cần hiệu năng M-series quá mức.', 'Chip A14 Bionic ổn định, Thiết kế mới, Cổng USB-C.', '12 tháng chính hãng', 14),

('Xiaomi Pad 6S Pro', 'Máy tính bảng hiệu năng cực mạnh, màn hình 144Hz.', 15000000, 17000000, 'Xiaomi', 20, 4, '/images/xiaomi-pad-6s-pro.jpg', 
    '{"screen": "12.4 inch 3K 144Hz", "chip": "Snapdragon 8 Gen 2", "ram": "12GB", "storage": "512GB", "features": "6 loa, Sạc 120W", "battery": "10000mAh"}', 
    4.7, 30, TRUE, TRUE, 
    'Game thủ và người dùng chuyên nghiệp cần máy tính bảng có hiệu năng mạnh tương đương laptop và sạc siêu nhanh.', 'Chip Snapdragon 8 Gen 2 mạnh mẽ, Màn hình 144Hz, Sạc nhanh 120W.', '18 tháng chính hãng', 7),

('Lenovo Tab M10 Plus (Gen 3)', 'Máy tính bảng giá rẻ, màn hình 2K.', 5500000, 6500000, 'Lenovo', 40, 4, '/images/lenovo-tab-m10.jpg', 
    '{"screen": "10.6 inch 2K LCD", "chip": "MediaTek Helio G80", "ram": "4GB", "storage": "128GB", "features": "Loa Quad Speakers", "battery": "7700mAh"}', 
    4.0, 90, FALSE, FALSE, 
    'Giải trí cơ bản, xem video, máy tính bảng cho trẻ em với màn hình 2K đẹp trong phân khúc giá rẻ.', 'Màn hình 2K, Giá thành rất phải chăng, Pin 7700mAh.', '12 tháng chính hãng', 7),

('Samsung Galaxy Tab S7 FE', 'Máy tính bảng màn hình lớn 12.4 inch, pin trâu.', 9000000, 11000000, 'Samsung', 25, 4, '/images/galaxy-tab-s7-fe.jpg', 
    '{"screen": "12.4 inch TFT LCD", "chip": "Snapdragon 778G", "ram": "6GB", "storage": "128GB", "features": "Kèm bút S Pen", "battery": "10090mAh"}', 
    4.5, 55, FALSE, FALSE, 
    'Sinh viên, giáo viên cần màn hình lớn để ghi chú, vẽ và học tập trong thời gian dài.', 'Màn hình lớn 12.4 inch, Pin 10090mAh, Kèm bút S Pen.', '12 tháng chính hãng', 14);